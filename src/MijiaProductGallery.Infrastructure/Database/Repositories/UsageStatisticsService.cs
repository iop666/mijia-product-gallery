using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>
/// 使用统计服务：今日/本周/本月从 UsageEvents 按本地自然边界计数；
/// 累计从 ProductUsages.TotalUseCount 汇总（事件被清理后累计仍然准确）。
/// 本周按自然周、周一起算。
/// </summary>
public sealed class UsageStatisticsService(
    IDbContextFactory<GalleryDbContext> contextFactory,
    TimeProvider? timeProvider = null) : IUsageStatisticsService
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    public async Task<UsageStatistics> GetStatisticsAsync(int topCount = 5, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var nowLocal = time.GetLocalNow();
        var todayStartUnix = StartOfDayUnix(nowLocal);
        var weekStartUnix = StartOfDayUnix(nowLocal.AddDays(-DaysSinceMonday(nowLocal.DayOfWeek)));
        var monthStartUnix = StartOfDayUnix(new DateTimeOffset(nowLocal.Year, nowLocal.Month, 1, 0, 0, 0, nowLocal.Offset));

        var todayCount = await context.UsageEvents
            .Where(e => e.UsedUnix >= todayStartUnix)
            .CountAsync(cancellationToken);
        var weekCount = await context.UsageEvents
            .Where(e => e.UsedUnix >= weekStartUnix)
            .CountAsync(cancellationToken);
        var monthCount = await context.UsageEvents
            .Where(e => e.UsedUnix >= monthStartUnix)
            .CountAsync(cancellationToken);
        var totalCount = await context.ProductUsages
            .SumAsync(u => (long?)u.TotalUseCount, cancellationToken) ?? 0;

        var top = await context.ProductUsages
            .AsNoTracking()
            .Where(u => u.TotalUseCount > 0)
            .OrderByDescending(u => u.TotalUseCount)
            .ThenBy(u => u.ProductId)
            .Take(topCount)
            .Join(context.Products.AsNoTracking(),
                usage => usage.ProductId,
                product => product.Id,
                (usage, product) => new TopUsedProduct
                {
                    ProductId = product.Id,
                    Model = product.Model,
                    Name = product.Name,
                    TotalUseCount = usage.TotalUseCount,
                })
            .ToListAsync(cancellationToken);

        return new UsageStatistics
        {
            TodayCount = todayCount,
            WeekCount = weekCount,
            MonthCount = monthCount,
            TotalCount = totalCount,
            TopProducts = top,
        };
    }

    /// <summary>图库数据概览：全部来自 Products 真实数据；大类合计恒等于产品总数。</summary>
    public async Task<GalleryOverview> GetGalleryOverviewAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var totalCount = await context.Products.CountAsync(cancellationToken);
        var withImageCount = await context.Products
            .CountAsync(p => p.Sha256 != null && p.ImagePath != null, cancellationToken);
        var removedCount = await context.Products
            .CountAsync(p => !p.IsAvailable, cancellationToken);
        var categories = await context.Products.AsNoTracking()
            .GroupBy(p => p.Category)
            .Select(g => new CategoryCount { Category = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Category)
            .ToListAsync(cancellationToken);

        return new GalleryOverview
        {
            TotalCount = totalCount,
            WithImageCount = withImageCount,
            WithoutImageCount = totalCount - withImageCount,
            RemovedCount = removedCount,
            Categories = categories,
        };
    }

    /// <summary>本地自然日起点的 Unix 秒。</summary>
    private static long StartOfDayUnix(DateTimeOffset local)
    {
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset).ToUnixTimeSeconds();
    }

    /// <summary>距离本周一（含当天）的天数：周一=0，周日=6。</summary>
    private static int DaysSinceMonday(DayOfWeek dayOfWeek)
    {
        return ((int)dayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
    }
}
