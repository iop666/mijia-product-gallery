using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Rules;
using MijiaProductGallery.Infrastructure.Http;

namespace MijiaProductGallery.Infrastructure.Sync;

/// <summary>
/// 同步引擎：获取分类 → 逐类获取产品 → 标准化归类 → 对比（CatalogDiff）→ 复核图片下载
/// → ImageStore 落盘 → 官方列白名单落库 → 缩略图 → 留痕与状态收口。
/// 全程经过对比规则层，不直接以接口数据改库；图片下载失败保持旧状态；
/// 单飞保护（进程级，手动与定时互斥），重入时返回进行中的一轮；遗留 Running 状态先重置再同步。
/// 支持外部取消：取消后本轮标记失败（原因"已取消"），可重试。
/// </summary>
public sealed class SyncEngine(
    IBaikeApiClient apiClient,
    IImageDownloader imageDownloader,
    IImageStore imageStore,
    IThumbnailService thumbnailService,
    IProductRepository products,
    ISyncStateRepository syncState,
    TimeProvider? timeProvider = null,
    ILogger<SyncEngine>? logger = null) : ISyncService
{
    private const int ImageDownloadConcurrency = 8;

    private readonly SemaphoreSlim singleFlight = new(1, 1);

    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private long currentRunId;
    private CancellationTokenSource? linkedCts;

    /// <summary>实时进度上报（工作线程触发，订阅方自行调度到 UI 线程）。</summary>
    public event Action<SyncProgress>? ProgressChanged;

    private long runStartedTicks;
    private long lastProgressTicks;

    private double ElapsedSeconds => (Environment.TickCount64 - runStartedTicks) / 1000.0;

    /// <summary>进度上报节流约 10Hz；阶段切换与收口用 force 确保穿透。</summary>
    private void RaiseRunning(SyncStage stage, string? detail, int done, int total, int failures, bool force = false)
    {
        var now = Environment.TickCount64;
        if (force)
        {
            Interlocked.Exchange(ref lastProgressTicks, now);
        }
        else
        {
            if (now - Interlocked.Read(ref lastProgressTicks) < 100)
            {
                return;
            }

            Interlocked.Exchange(ref lastProgressTicks, now);
        }

        Raise(new SyncProgress
        {
            Status = SyncStatus.Running,
            Stage = stage,
            StageIndex = SyncProgress.StageOrder(stage),
            Detail = detail,
            Done = done,
            Total = total,
            ElapsedSeconds = ElapsedSeconds,
            OverallPercent = SyncProgress.EstimateOverallPercent(stage, done, total),
            Failures = failures,
        });
    }

    private void RaiseTerminal(SyncStage stage, SyncStatus status, SyncRunCounts? counts, string? errorMessage)
    {
        Raise(new SyncProgress
        {
            Status = status,
            Stage = stage,
            StageIndex = SyncProgress.StageOrder(stage),
            ElapsedSeconds = ElapsedSeconds,
            OverallPercent = status == SyncStatus.Success ? 100 : null,
            Failures = counts?.ImageFailureCount ?? 0,
            Counts = counts,
            ErrorMessage = errorMessage,
        });
    }

    /// <summary>订阅者异常只记日志，不影响同步本身。</summary>
    private void Raise(SyncProgress progress)
    {
        try
        {
            ProgressChanged?.Invoke(progress);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "同步进度订阅者异常（已忽略）");
        }
    }

    public async Task<SyncRun> SyncNowAsync(SyncTrigger trigger, CancellationToken cancellationToken = default)
    {
        if (!singleFlight.Wait(0, cancellationToken))
        {
            var activeRunId = Interlocked.Read(ref currentRunId);
            return await syncState.GetRunAsync(activeRunId, cancellationToken)
                ?? throw new InvalidOperationException("同步正在进行，但缺少可返回的运行记录");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Interlocked.Exchange(ref linkedCts, linked);
        try
        {
            return await RunAsync(trigger, linked.Token);
        }
        finally
        {
            Interlocked.Exchange(ref linkedCts, null);
            singleFlight.Release();
        }
    }

    /// <summary>请求取消当前进行中的一轮同步（无进行中轮次为无操作）。</summary>
    public Task CancelAsync(CancellationToken cancellationToken = default)
    {
        linkedCts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task<SyncRun> RunAsync(SyncTrigger trigger, CancellationToken cancellationToken)
    {
        var state = await syncState.GetStateAsync(cancellationToken);
        if (state.Status == SyncStatus.Running)
        {
            await syncState.SaveStateAsync(
                state with { Status = SyncStatus.Failed, ErrorMessage = "上次同步异常中断，已重置" },
                cancellationToken);
        }

        var runId = await syncState.StartRunAsync(trigger, NowUnix(), cancellationToken);
        Interlocked.Exchange(ref currentRunId, runId);
        var currentStage = SyncStage.FetchingCategories;
        var failures = new FailureCounter();
        var storedImages = new List<ImageStoreResult>();
        var appliedChanges = new List<ProductChange>();

        try
        {
            runStartedTicks = Environment.TickCount64;
            await SetStageAsync(runId, SyncStage.FetchingCategories, cancellationToken);
            RaiseRunning(SyncStage.FetchingCategories, null, 0, 0, 0, force: true);
            var categories = await apiClient.GetCategoriesAsync(cancellationToken);
            var ptIdToName = categories.ToDictionary(category => category.PtId, category => category.Name);

            currentStage = SyncStage.FetchingProducts;
            await SetStageAsync(runId, SyncStage.FetchingProducts, cancellationToken);
            RaiseRunning(SyncStage.FetchingProducts, $"分类 0/{categories.Count}", 0, categories.Count, 0, force: true);
            var (dtoByModel, membership) = await FetchAllProductsAsync(
                categories,
                (index, found) => RaiseRunning(
                    SyncStage.FetchingProducts,
                    $"分类 {index}/{categories.Count} · 已发现 {found:N0} 个型号",
                    index,
                    categories.Count,
                    failures.Count,
                    force: index >= categories.Count),
                cancellationToken);
            var remote = BuildRemoteStates(dtoByModel, membership, ptIdToName);
            var remoteByModel = remote.ToDictionary(item => item.Model, StringComparer.Ordinal);

            currentStage = SyncStage.Comparing;
            await SetStageAsync(runId, SyncStage.Comparing, cancellationToken);
            var localRows = await products.GetAllAsync(cancellationToken);
            var local = localRows.Select(LocalProductState.FromProduct).ToList();
            RaiseRunning(SyncStage.Comparing, $"本地 {local.Count:N0} · 远端 {remote.Count:N0}", 0, 0, failures.Count, force: true);

            currentStage = SyncStage.DownloadingImages;
            await SetStageAsync(runId, SyncStage.DownloadingImages, cancellationToken);
            RaiseRunning(SyncStage.DownloadingImages, null, 0, 0, failures.Count, force: true);
            var reviews = await DownloadImageReviewsAsync(
                remoteByModel,
                local,
                failures,
                (done, total, failureCount) => RaiseRunning(
                    SyncStage.DownloadingImages,
                    $"{done:N0}/{total:N0}",
                    done,
                    total,
                    failureCount,
                    force: done >= total),
                cancellationToken);
            await AdoptVerifiedTimestampsAsync(local, remoteByModel, reviews.Inputs, cancellationToken);

            var diff = ProductCompareRules.CompareCatalog(local, remote, reviews.Inputs);

            currentStage = SyncStage.UpdatingDatabase;
            await SetStageAsync(runId, SyncStage.UpdatingDatabase, cancellationToken);
            RaiseRunning(SyncStage.UpdatingDatabase, $"变更 0/{diff.Changes.Count}", 0, diff.Changes.Count, failures.Count, force: true);
            for (var changeIndex = 0; changeIndex < diff.Changes.Count; changeIndex++)
            {
                var applied = await ApplyChangeAsync(diff.Changes[changeIndex], remoteByModel, reviews.Images, storedImages, failures, cancellationToken);
                if (applied is not null)
                {
                    appliedChanges.Add(applied);
                }

                RaiseRunning(
                    SyncStage.UpdatingDatabase,
                    $"变更 {changeIndex + 1}/{diff.Changes.Count}",
                    changeIndex + 1,
                    diff.Changes.Count,
                    failures.Count,
                    force: changeIndex + 1 >= diff.Changes.Count);
            }

            await syncState.AddChangesAsync(runId, appliedChanges, cancellationToken);

            currentStage = SyncStage.GeneratingThumbnails;
            await SetStageAsync(runId, SyncStage.GeneratingThumbnails, cancellationToken);
            await GenerateThumbnailsAsync(
                storedImages,
                (done, total) => RaiseRunning(SyncStage.GeneratingThumbnails, $"{done:N0}/{total:N0}", done, total, failures.Count, force: done >= total),
                cancellationToken);
            RaiseRunning(
                SyncStage.GeneratingThumbnails,
                $"{storedImages.Count:N0}/{storedImages.Count:N0}",
                storedImages.Count,
                storedImages.Count,
                failures.Count,
                force: true);

            var counts = new SyncRunCounts
            {
                NewCount = diff.Count(ChangeType.New),
                DelistedCount = diff.Count(ChangeType.Delisted),
                CategoryChangedCount = diff.Count(ChangeType.CategoryChanged),
                NameChangedCount = diff.Count(ChangeType.NameChanged),
                ImageChangedCount = diff.Count(ChangeType.ImageChanged),
                IdReusedCount = diff.Count(ChangeType.IdReused),
                ImageFailureCount = failures.Count,
            };
            await syncState.CompleteRunAsync(runId, SyncStatus.Success, SyncStage.Completed, counts, null, NowUnix(), cancellationToken);
            await SaveStateAsync(SyncStatus.Success, SyncStage.Completed, null, cancellationToken);
            RaiseTerminal(SyncStage.Completed, SyncStatus.Success, counts, null);
            return await syncState.GetRunAsync(runId, cancellationToken)
                ?? throw new InvalidOperationException("同步完成但缺少运行记录");
        }
        catch (OperationCanceledException)
        {
            await CompleteRunFailedAsync(runId, currentStage, failures, "同步已取消", CancellationToken.None);
            RaiseTerminal(currentStage, SyncStatus.Failed, new SyncRunCounts { ImageFailureCount = failures.Count }, "同步已取消");
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "同步失败于阶段 {Stage}", currentStage);
            await CompleteRunFailedAsync(runId, currentStage, failures, exception.Message, CancellationToken.None);
            RaiseTerminal(currentStage, SyncStatus.Failed, new SyncRunCounts { ImageFailureCount = failures.Count }, exception.Message);
            return await syncState.GetRunAsync(runId, cancellationToken)
                ?? throw new InvalidOperationException("同步失败但缺少运行记录", exception);
        }
        finally
        {
            Interlocked.Exchange(ref currentRunId, 0);
        }
    }

    private async Task<(Dictionary<string, BaikeProductDto> Dtos, Dictionary<string, List<int>> Membership)> FetchAllProductsAsync(
        IReadOnlyList<BaikeCategory> categories,
        Action<int, int> onCategoryScanned,
        CancellationToken cancellationToken)
    {
        var dtoByModel = new Dictionary<string, BaikeProductDto>(StringComparer.Ordinal);
        var membership = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var index = 0; index < categories.Count; index++)
        {
            var category = categories[index];
            var list = await apiClient.GetProductsByCategoryAsync(category.PtId, cancellationToken);
            foreach (var dto in list)
            {
                var model = dto.Model.Trim();
                if (model.Length == 0)
                {
                    continue;
                }

                if (!membership.TryGetValue(model, out var categoryIds))
                {
                    membership[model] = [category.PtId];
                    dtoByModel[model] = dto;
                }
                else if (!categoryIds.Contains(category.PtId))
                {
                    // 同型号出现在多个分类：归类判定由 CategoryRules 处理，此处仅完整记录成员关系。
                    categoryIds.Add(category.PtId);
                }
            }

            onCategoryScanned(index + 1, dtoByModel.Count);
        }

        return (dtoByModel, membership);
    }

    private List<RemoteProductState> BuildRemoteStates(
        Dictionary<string, BaikeProductDto> dtoByModel,
        Dictionary<string, List<int>> membership,
        Dictionary<int, string> ptIdToName)
    {
        var remote = new List<RemoteProductState>(dtoByModel.Count);
        foreach (var (model, dto) in dtoByModel.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var category = CategoryRules.ResolveCategory(membership[model], ptIdToName);
            var normalized = BaikeProductNormalizer.Normalize(dto, category);
            if (UrlRules.TryNormalizeImageUrl(dto.RealIcon, out var normalizedUrl))
            {
                normalized = normalized with { RealIconUrl = normalizedUrl };
            }

            remote.Add(normalized);
        }

        return remote;
    }

    private async Task<ReviewOutcome> DownloadImageReviewsAsync(
        Dictionary<string, RemoteProductState> remoteByModel,
        List<LocalProductState> local,
        FailureCounter failures,
        Action<int, int, int> onProgress,
        CancellationToken cancellationToken)
    {
        var localByModel = local.ToDictionary(item => item.Model, StringComparer.Ordinal);
        var reviewModels = new List<string>();
        foreach (var (model, remoteItem) in remoteByModel)
        {
            var localItem = localByModel.GetValueOrDefault(model);
            var hasImageUrl = !string.IsNullOrWhiteSpace(remoteItem.RealIconUrl);
            var needsReview = localItem is null
                ? hasImageUrl
                : (localItem.ImageSha256 is null && hasImageUrl)
                    || localItem.UpdateTimeUnix is null
                    || (localItem.UpdateTimeUnix is { } localUpdate && localUpdate != remoteItem.UpdateTimeUnix);
            if (needsReview)
            {
                reviewModels.Add(model);
            }
        }

        var completed = 0;
        var total = reviewModels.Count;
        var images = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        var inputs = new ConcurrentDictionary<string, ImageReviewInput>(StringComparer.Ordinal);
        using var throttle = new SemaphoreSlim(ImageDownloadConcurrency, ImageDownloadConcurrency);
        var tasks = reviewModels.Select(async model =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var url = remoteByModel[model].RealIconUrl;
                if (string.IsNullOrWhiteSpace(url))
                {
                    return;
                }

                try
                {
                    var bytes = await imageDownloader.DownloadAsync(url, cancellationToken);
                    images[model] = bytes;
                    inputs[model] = new ImageReviewInput
                    {
                        DownloadedSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                    };
                }
                catch (Exception exception) when (exception is ImageDownloadException or HttpRequestException)
                {
                    failures.Increment();
                    logger?.LogWarning(exception, "图片复核下载失败，保持本地旧状态：{Model}", model);
                }
            }
            finally
            {
                throttle.Release();
                onProgress(Interlocked.Increment(ref completed), total, failures.Count);
            }
        });
        await Task.WhenAll(tasks);
        onProgress(total, total, failures.Count);
        return new ReviewOutcome(images, inputs);
    }

    /// <summary>
    /// 对"下载 SHA 与本地一致"的复核采纳官网更新时间：种子导入行无时间戳，
    /// 采纳后不再每轮重复复核；本步只写白名单列，不产生任何数据变化。
    /// </summary>
    private async Task AdoptVerifiedTimestampsAsync(
        List<LocalProductState> local,
        Dictionary<string, RemoteProductState> remoteByModel,
        ConcurrentDictionary<string, ImageReviewInput> inputs,
        CancellationToken cancellationToken)
    {
        if (inputs.IsEmpty)
        {
            return;
        }

        var localByModel = local.ToDictionary(item => item.Model, StringComparer.Ordinal);
        var adoptions = new List<(string Model, long UpdateTimeUnix)>();
        foreach (var (model, input) in inputs)
        {
            var localItem = localByModel.GetValueOrDefault(model);
            var remoteItem = remoteByModel[model];
            if (localItem is not null
                && string.Equals(localItem.ImageSha256, input.DownloadedSha256, StringComparison.OrdinalIgnoreCase)
                && localItem.UpdateTimeUnix != remoteItem.UpdateTimeUnix)
            {
                adoptions.Add((model, remoteItem.UpdateTimeUnix));
            }
        }

        if (adoptions.Count > 0)
        {
            logger?.LogInformation("复核采纳官网时间戳：{Count} 行", adoptions.Count);
            await products.UpdateSyncTimestampsAsync(adoptions, NowUnix(), cancellationToken);
        }
    }

    private async Task<ProductChange?> ApplyChangeAsync(
        ProductChange change,
        Dictionary<string, RemoteProductState> remoteByModel,
        ConcurrentDictionary<string, byte[]> reviewImages,
        List<ImageStoreResult> storedImages,
        FailureCounter failures,
        CancellationToken cancellationToken)
    {
        switch (change.Type)
        {
            case ChangeType.None:
                return null;
            case ChangeType.New:
            {
                var remote = remoteByModel[change.Model];
                var product = new Product
                {
                    Model = change.Model,
                    Name = change.NewName ?? change.Model,
                    Brand = change.NewBrand ?? string.Empty,
                    Category = change.NewCategory ?? CategoryRules.UnknownCategoryName,
                    ImageUrl = change.ImageUrl,
                    IsAvailable = true,
                    CreateTimeUnix = remote.CreateTimeUnix,
                    UpdateTimeUnix = remote.UpdateTimeUnix,
                    FirstSeenUnix = NowUnix(),
                    LastSeenUnix = NowUnix(),
                    RandomKey = Random.Shared.NextInt64(),
                };
                if (reviewImages.TryGetValue(change.Model, out var bytes))
                {
                    try
                    {
                        var result = await imageStore.StoreNewAsync(change.Model, new MemoryStream(bytes), cancellationToken);
                        ApplyImage(product, result, change.ImageUrl);
                        if (result.Status != ImageStoreStatus.Unchanged)
                        {
                            storedImages.Add(result);
                        }
                    }
                    catch (ImageValidationException exception)
                    {
                        failures.Increment();
                        logger?.LogWarning(exception, "新型号图片校验失败，先入库无图记录：{Model}", change.Model);
                    }
                }

                await products.AddAsync(product, cancellationToken);
                return change;
            }
            case ChangeType.Delisted:
            {
                var row = await products.GetByModelAsync(change.Model, cancellationToken);
                if (row is null)
                {
                    return null;
                }

                row.IsAvailable = false;
                await products.UpdateOfficialFieldsAsync(row, cancellationToken);
                return change;
            }
            default:
            {
                // Relisted / CategoryChanged / NameChanged / ImageChanged / IdReused
                var row = await products.GetByModelAsync(change.Model, cancellationToken);
                if (row is null)
                {
                    return null;
                }

                if (change.Type == ChangeType.Relisted)
                {
                    row.IsAvailable = true;
                }

                if (change.NewName is not null)
                {
                    row.Name = change.NewName;
                }

                if (change.NewBrand is not null)
                {
                    row.Brand = change.NewBrand;
                }

                if (change.NewCategory is not null)
                {
                    row.Category = change.NewCategory;
                }

                // 本轮已对照官网：采纳官网更新时间；种子导入行的空创建时间一并补齐。
                var remoteState = remoteByModel[change.Model];
                row.UpdateTimeUnix = remoteState.UpdateTimeUnix;
                row.CreateTimeUnix ??= remoteState.CreateTimeUnix;

                ImageStoreResult? storeResult = null;
                if (change.ImageOutcome is ImageOutcome.ImageChanged or ImageOutcome.IdReused
                    && reviewImages.TryGetValue(change.Model, out var bytes))
                {
                    try
                    {
                        storeResult = change.ImageOutcome == ImageOutcome.IdReused || row.ImageFileName is null
                            ? await imageStore.ReplaceWithHistoryAsync(change.Model, row.ImageFileName ?? string.Empty, new MemoryStream(bytes), cancellationToken)
                            : await imageStore.ReplaceAsync(change.Model, row.ImageFileName, new MemoryStream(bytes), cancellationToken);
                        if (storeResult.Status != ImageStoreStatus.Unchanged)
                        {
                            ApplyImage(row, storeResult, change.ImageUrl);
                            storedImages.Add(storeResult);
                        }
                    }
                    catch (ImageValidationException exception)
                    {
                        failures.Increment();
                        logger?.LogWarning(exception, "图片落盘校验失败，保持本地旧图：{Model}", change.Model);
                    }
                }

                row.LastSeenUnix = NowUnix();
                await products.UpdateOfficialFieldsAsync(row, cancellationToken);
                return change.ImageOutcome == ImageOutcome.IdReused && storeResult is not null
                    ? change with { ReplacedByOldFileName = storeResult.ReplacedByOldFileName }
                    : change;
            }
        }
    }

    private static void ApplyImage(Product row, ImageStoreResult result, string? imageUrl)
    {
        row.ImageFileName = result.ImageFileName;
        row.ImagePath = result.ImagePath;
        row.ImageFormat = result.Format;
        row.ImageWidth = result.Width;
        row.ImageHeight = result.Height;
        row.FileSize = result.FileSize;
        row.Sha256 = result.Sha256;
        row.ImageUrl = imageUrl ?? row.ImageUrl;
    }

    private async Task GenerateThumbnailsAsync(
        IReadOnlyList<ImageStoreResult> storedImages,
        Action<int, int> onProgress,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < storedImages.Count; index++)
        {
            try
            {
                await thumbnailService.EnsureThumbnailAsync(storedImages[index].ImageFileName, storedImages[index].Sha256, cancellationToken);
            }
            catch (Exception exception) when (exception is ImageValidationException or FileNotFoundException or IOException)
            {
                logger?.LogWarning(exception, "缩略图生成失败（不影响图库数据）：{File}", storedImages[index].ImageFileName);
            }

            onProgress(index + 1, storedImages.Count);
        }
    }

    private async Task SetStageAsync(long runId, SyncStage stage, CancellationToken cancellationToken)
    {
        var state = await syncState.GetStateAsync(cancellationToken);
        await syncState.SaveStateAsync(state with { Status = SyncStatus.Running, Stage = stage }, cancellationToken);
    }

    private async Task SaveStateAsync(SyncStatus status, SyncStage stage, string? errorMessage, CancellationToken cancellationToken)
    {
        var state = await syncState.GetStateAsync(cancellationToken);
        await syncState.SaveStateAsync(
            state with
            {
                Status = status,
                Stage = stage,
                ErrorMessage = errorMessage,
                LastSyncUnix = NowUnix(),
                LastSuccessfulSyncUnix = status == SyncStatus.Success ? NowUnix() : state.LastSuccessfulSyncUnix,
                SnapshotDate = time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            },
            cancellationToken);
    }

    private async Task CompleteRunFailedAsync(
        long runId,
        SyncStage stage,
        FailureCounter failures,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await syncState.CompleteRunAsync(runId, SyncStatus.Failed, stage, new SyncRunCounts { ImageFailureCount = failures.Count }, errorMessage, NowUnix(), cancellationToken);
            await SaveStateAsync(SyncStatus.Failed, stage, errorMessage, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogError(exception, "同步失败状态收口也失败");
        }
    }

    private long NowUnix()
    {
        return time.GetUtcNow().ToUnixTimeSeconds();
    }

    private sealed class FailureCounter
    {
        private int count;

        public int Count => count;

        public void Increment()
        {
            Interlocked.Increment(ref count);
        }
    }

    private sealed record ReviewOutcome(
        ConcurrentDictionary<string, byte[]> Images,
        ConcurrentDictionary<string, ImageReviewInput> Inputs);
}
