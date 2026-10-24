using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>
/// 最近使用视图服务：UsageEvents 按产品聚合 MAX(UsedUnix) 数据库侧排序，
/// 时间并列时按型号次序稳定排序；产品行与最近事件按批补齐，避免逐条往返。
/// 应用内每次调用使用独立上下文。
/// </summary>
public sealed class RecentService : IRecentService
{
    private readonly GalleryDbContext? shared;
    private readonly IDbContextFactory<GalleryDbContext>? factory;

    public RecentService(GalleryDbContext context) => shared = context;

    public RecentService(IDbContextFactory<GalleryDbContext> factory) => this.factory = factory;

    private DbContextScope Rent() => new(factory, shared);

    /// <summary>
    /// 分页获取最近使用：跳过/取行在数据库侧完成（Skip/Take 聚合查询），
    /// 仅当前页补齐事件类型与产品行；总产品数数据库侧 COUNT(DISTINCT)。
    /// </summary>
    public async Task<RecentPageResult> GetRecentPageAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Max(1, take);

        await using var scope = Rent();
        var context = scope.Context;

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

        var aggregates = grouped
            .Select(g => new RecentAggregate(g.ProductId, g.LastUsedUnix))
            .ToList();
        var items = await MaterializeAsync(context, aggregates, cancellationToken);
        return new RecentPageResult(items, totalCount);
    }

    public async Task<IReadOnlyList<RecentProduct>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        limit = Math.Max(1, limit);

        await using var scope = Rent();
        var context = scope.Context;

        // 聚合在数据库侧完成；多取一段以覆盖同秒并列后按型号排序的情况。
        var grouped = await context.UsageEvents
            .GroupBy(e => e.ProductId)
            .Select(g => new { ProductId = g.Key, LastUsedUnix = g.Max(e => e.UsedUnix) })
            .OrderByDescending(g => g.LastUsedUnix)
            .Take(limit * 2)
            .ToListAsync(cancellationToken);

        var aggregates = grouped
            .Select(g => new RecentAggregate(g.ProductId, g.LastUsedUnix))
            .ToList();
        var items = await MaterializeAsync(context, aggregates, cancellationToken);
        return items
            .OrderByDescending(item => item.LastUsedUnix)
            .ThenBy(item => item.Product.Model, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    /// <summary>按批取产品行与每产品最近事件，保持传入聚合顺序。</summary>
    private static async Task<List<RecentProduct>> MaterializeAsync(
        GalleryDbContext context,
        IReadOnlyList<RecentAggregate> aggregates,
        CancellationToken cancellationToken)
    {
        if (aggregates.Count == 0)
        {
            return [];
        }

        var ids = aggregates.Select(a => a.ProductId).Distinct().ToList();
        var products = await context.Products
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p, cancellationToken);

        var events = await context.UsageEvents
            .AsNoTracking()
            .Where(e => ids.Contains(e.ProductId))
            .OrderByDescending(e => e.UsedUnix)
            .ThenByDescending(e => e.Id)
            .ToListAsync(cancellationToken);
        var lastByProduct = new Dictionary<int, UsageType>();
        foreach (var usageEvent in events)
        {
            lastByProduct.TryAdd(usageEvent.ProductId, usageEvent.Type);
        }

        var result = new List<RecentProduct>(aggregates.Count);
        foreach (var aggregate in aggregates)
        {
            if (!products.TryGetValue(aggregate.ProductId, out var product))
            {
                continue;
            }

            result.Add(new RecentProduct
            {
                Product = product,
                LastUsedUnix = aggregate.LastUsedUnix,
                LastEvent = lastByProduct.GetValueOrDefault(product.Id, UsageType.View),
            });
        }

        return result;
    }

    private sealed record RecentAggregate(int ProductId, long LastUsedUnix);

    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        var context = scope.Context;
        await context.UsageEvents.ExecuteDeleteAsync(cancellationToken);
        await context.ProductUsages.ExecuteDeleteAsync(cancellationToken);
    }
}
