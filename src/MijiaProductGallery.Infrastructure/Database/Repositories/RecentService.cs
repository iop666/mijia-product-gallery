using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>
/// 最近使用视图服务：UsageEvents 按产品聚合 MAX(UsedUnix) 数据库侧排序，
/// 时间并列时按型号次序稳定排序；最近事件类型与产品行二次查询补齐。
/// </summary>
public sealed class RecentService(GalleryDbContext context) : IRecentService
{
    /// <summary>
    /// 分页获取最近使用：跳过/取行在数据库侧完成（Skip/Take 聚合查询），
    /// 仅当前页补齐事件类型与产品行；总产品数数据库侧 COUNT(DISTINCT)。
    /// </summary>
    public async Task<RecentPageResult> GetRecentPageAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Max(1, take);

        var totalCount = await context.UsageEvents
            .Select(e => e.ProductId)
            .Distinct()
            .CountAsync(cancellationToken);

        var grouped = await context.UsageEvents
            .GroupBy(e => e.ProductId)
            .Select(g => new { ProductId = g.Key, LastUsedUnix = g.Max(e => e.UsedUnix) })
            .OrderByDescending(g => g.LastUsedUnix)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var items = new List<RecentProduct>(grouped.Count);
        foreach (var g in grouped)
        {
            var product = await context.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == g.ProductId, cancellationToken);
            if (product is null)
            {
                continue;
            }

            var lastEvent = await context.UsageEvents
                .AsNoTracking()
                .Where(e => e.ProductId == g.ProductId && e.UsedUnix == g.LastUsedUnix)
                .OrderByDescending(e => e.Id)
                .Select(e => (UsageType?)e.Type)
                .FirstOrDefaultAsync(cancellationToken) ?? UsageType.View;
            items.Add(new RecentProduct
            {
                Product = product,
                LastUsedUnix = g.LastUsedUnix,
                LastEvent = lastEvent,
            });
        }

        return new RecentPageResult(items, totalCount);
    }

    public async Task<IReadOnlyList<RecentProduct>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        limit = Math.Max(1, limit);

        // 聚合在数据库侧完成；时间并列时按型号次序稳定排序（内存内对已聚合的小集合执行）。
        var grouped = await context.UsageEvents
            .GroupBy(e => e.ProductId)
            .Select(g => new { ProductId = g.Key, LastUsedUnix = g.Max(e => e.UsedUnix) })
            .ToListAsync(cancellationToken);
        var productsById = await context.Products
            .AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p, cancellationToken);

        grouped.Sort((left, right) =>
        {
            var byTime = right.LastUsedUnix.CompareTo(left.LastUsedUnix);
            if (byTime != 0)
            {
                return byTime;
            }

            var leftModel = productsById.GetValueOrDefault(left.ProductId)?.Model ?? string.Empty;
            var rightModel = productsById.GetValueOrDefault(right.ProductId)?.Model ?? string.Empty;
            return string.CompareOrdinal(leftModel, rightModel);
        });

        var result = new List<RecentProduct>(limit);
        foreach (var g in grouped)
        {
            if (!productsById.TryGetValue(g.ProductId, out var product))
            {
                continue;
            }

            var lastEvent = await LastEventTypeAsync(g.ProductId, g.LastUsedUnix, cancellationToken);
            result.Add(new RecentProduct
            {
                Product = product,
                LastUsedUnix = g.LastUsedUnix,
                LastEvent = lastEvent,
            });

            if (result.Count >= limit)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>取产品在指定时间点的最近一次事件类型（同秒并列时取 Id 最大的一条）。</summary>
    private async Task<UsageType> LastEventTypeAsync(
        int productId,
        long usedUnix,
        CancellationToken cancellationToken)
    {
        var last = await context.UsageEvents
            .AsNoTracking()
            .Where(e => e.ProductId == productId && e.UsedUnix == usedUnix)
            .OrderByDescending(e => e.Id)
            .Select(e => (UsageType?)e.Type)
            .FirstOrDefaultAsync(cancellationToken);
        return last ?? UsageType.View;
    }

    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        await context.UsageEvents.ExecuteDeleteAsync(cancellationToken);
        await context.ProductUsages.ExecuteDeleteAsync(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}

