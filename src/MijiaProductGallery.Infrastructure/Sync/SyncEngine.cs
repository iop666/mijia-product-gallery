using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Globalization;
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
/// 单飞保护，重入时返回进行中的一轮；遗留 Running 状态先重置再同步。
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

    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim singleFlight = new(1, 1);
    private long currentRunId;

    public async Task<SyncRun> SyncNowAsync(SyncTrigger trigger, CancellationToken cancellationToken = default)
    {
        if (!await singleFlight.WaitAsync(0, cancellationToken))
        {
            var activeRunId = Interlocked.Read(ref currentRunId);
            return await syncState.GetRunAsync(activeRunId, cancellationToken)
                ?? throw new InvalidOperationException("同步正在进行，但缺少可返回的运行记录");
        }

        try
        {
            return await RunAsync(trigger, cancellationToken);
        }
        finally
        {
            Interlocked.Exchange(ref currentRunId, 0);
            singleFlight.Release();
        }
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
            await SetStageAsync(runId, SyncStage.FetchingCategories, cancellationToken);
            var categories = await apiClient.GetCategoriesAsync(cancellationToken);
            var ptIdToName = categories.ToDictionary(category => category.PtId, category => category.Name);

            currentStage = SyncStage.FetchingProducts;
            await SetStageAsync(runId, SyncStage.FetchingProducts, cancellationToken);
            var (dtoByModel, membership) = await FetchAllProductsAsync(categories, cancellationToken);
            var remote = BuildRemoteStates(dtoByModel, membership, ptIdToName);
            var remoteByModel = remote.ToDictionary(item => item.Model, StringComparer.Ordinal);

            currentStage = SyncStage.Comparing;
            await SetStageAsync(runId, SyncStage.Comparing, cancellationToken);
            var localRows = await products.GetAllAsync(cancellationToken);
            var local = localRows.Select(LocalProductState.FromProduct).ToList();

            currentStage = SyncStage.DownloadingImages;
            await SetStageAsync(runId, SyncStage.DownloadingImages, cancellationToken);
            var reviews = await DownloadImageReviewsAsync(remoteByModel, local, failures, cancellationToken);

            var diff = ProductCompareRules.CompareCatalog(local, remote, reviews.Inputs);

            currentStage = SyncStage.UpdatingDatabase;
            await SetStageAsync(runId, SyncStage.UpdatingDatabase, cancellationToken);
            foreach (var change in diff.Changes)
            {
                var applied = await ApplyChangeAsync(change, remoteByModel, reviews.Images, storedImages, failures, cancellationToken);
                if (applied is not null)
                {
                    appliedChanges.Add(applied);
                }
            }

            await syncState.AddChangesAsync(runId, appliedChanges, cancellationToken);

            currentStage = SyncStage.GeneratingThumbnails;
            await SetStageAsync(runId, SyncStage.GeneratingThumbnails, cancellationToken);
            await GenerateThumbnailsAsync(storedImages, cancellationToken);

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
            return await syncState.GetRunAsync(runId, cancellationToken)
                ?? throw new InvalidOperationException("同步完成但缺少运行记录");
        }
        catch (OperationCanceledException)
        {
            await CompleteRunFailedAsync(runId, currentStage, failures, "同步已取消", CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "同步失败于阶段 {Stage}", currentStage);
            await CompleteRunFailedAsync(runId, currentStage, failures, exception.Message, CancellationToken.None);
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
        CancellationToken cancellationToken)
    {
        var dtoByModel = new Dictionary<string, BaikeProductDto>(StringComparer.Ordinal);
        var membership = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var category in categories)
        {
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
                    || (localItem.UpdateTimeUnix is { } localUpdate && localUpdate != remoteItem.UpdateTimeUnix);
            if (needsReview)
            {
                reviewModels.Add(model);
            }
        }

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
            }
        });
        await Task.WhenAll(tasks);
        return new ReviewOutcome(images, inputs);
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

    private async Task GenerateThumbnailsAsync(IReadOnlyList<ImageStoreResult> storedImages, CancellationToken cancellationToken)
    {
        foreach (var image in storedImages)
        {
            try
            {
                await thumbnailService.EnsureThumbnailAsync(image.ImageFileName, image.Sha256, cancellationToken);
            }
            catch (Exception exception) when (exception is ImageValidationException or FileNotFoundException or IOException)
            {
                logger?.LogWarning(exception, "缩略图生成失败（不影响图库数据）：{File}", image.ImageFileName);
            }
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
