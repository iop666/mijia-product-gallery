using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Http;

namespace MijiaProductGallery.Infrastructure.Seed;

/// <summary>
/// 首次启动初始化：建库 → 已有产品则直接返回 → 按约定位置查种子包并导入；
/// 无种子时探测网络，可达则在线全量同步，不可达则返回显式失败状态（附可用操作）。
/// </summary>
public sealed class FirstRunInitializer(
    DatabasePaths paths,
    DbInitializer dbInitializer,
    GalleryDbContext dbContext,
    ISeedImporter seedImporter,
    IBaikeApiClient apiClient,
    ISyncService syncService,
    ILogger<FirstRunInitializer>? logger = null) : IFirstRunInitializer
{
    public async Task<InitializationReport> InitializeAsync(
        string? explicitSeedPath = null,
        IProgress<SeedImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await dbInitializer.InitializeAsync(cancellationToken);

        if (await dbContext.Products.AnyAsync(cancellationToken))
        {
            var existingSnapshot = await dbContext.SyncState.AsNoTracking()
                .Where(s => s.Id == 1)
                .Select(s => s.SnapshotDate)
                .FirstOrDefaultAsync(cancellationToken);
            return new InitializationReport
            {
                Outcome = InitializationOutcome.AlreadyInitialized,
                SnapshotDate = existingSnapshot,
            };
        }

        var seedPath = explicitSeedPath ?? FindSeedPackage();
        if (seedPath is not null)
        {
            logger?.LogInformation("发现种子包：{Path}", seedPath);
            try
            {
                var result = await seedImporter.ImportAsync(seedPath, progress, cancellationToken);
                return new InitializationReport
                {
                    Outcome = InitializationOutcome.CompletedFromSeed,
                    SnapshotDate = result.SnapshotDate,
                    SeedResult = result,
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger?.LogError(exception, "种子包导入失败");
                return Fail(InitializationOutcome.FailedImport, exception.Message, ["retry", "check-seed", "online-init"]);
            }
        }

        // 无种子包：探测网络，可达则在线初始化，否则显式失败。
        try
        {
            _ = await apiClient.GetCategoriesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or BaikeApiException)
        {
            logger?.LogWarning(exception, "网络不可达且无种子包");
            return Fail(InitializationOutcome.FailedNoSeedOffline,
                $"无法连接米家百科且未找到种子包：{exception.Message}",
                ["retry", "check-seed", "online-init"]);
        }

        try
        {
            var run = await syncService.SyncNowAsync(SyncTrigger.Manual, cancellationToken);
            if (run.Status != SyncStatus.Success)
            {
                return Fail(InitializationOutcome.FailedNoSeedOffline, $"在线初始化未完成：{run.ErrorMessage}", ["retry", "online-init"]);
            }

            var snapshot = await dbContext.SyncState.AsNoTracking()
                .Where(s => s.Id == 1)
                .Select(s => s.SnapshotDate)
                .FirstOrDefaultAsync(cancellationToken);
            return new InitializationReport
            {
                Outcome = InitializationOutcome.CompletedOnline,
                SnapshotDate = snapshot,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Fail(InitializationOutcome.FailedNoSeedOffline, $"在线初始化失败：{exception.Message}", ["retry", "online-init"]);
        }
    }

    /// <summary>种子包查找顺序：exe 目录 → 数据根 seed\ 目录，各取文件名最大的 seed-*.zip。</summary>
    private string? FindSeedPackage()
    {
        foreach (var directory in new[] { AppContext.BaseDirectory, paths.SeedDirectory })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            var newest = Directory.GetFiles(directory, "seed-*.zip")
                .OrderDescending(StringComparer.Ordinal)
                .FirstOrDefault();
            if (newest is not null)
            {
                return newest;
            }
        }

        return null;
    }

    private static InitializationReport Fail(
        InitializationOutcome outcome,
        string message,
        IReadOnlyList<string> actions)
    {
        return new InitializationReport
        {
            Outcome = outcome,
            Message = message,
            AvailableActions = actions,
        };
    }
}
