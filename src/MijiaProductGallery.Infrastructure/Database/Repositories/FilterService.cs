using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>
/// 筛选条件翻译：ProductFilter → IQueryable 组合（维度 AND、维度内 IN）。
/// 使用次数维度需要跨表子查询，由组合查询服务在同一上下文内翻译（见 ApplyUsage）。
/// </summary>
public sealed class FilterService : IFilterService
{
    private readonly TimeProvider time;

    public FilterService(TimeProvider? timeProvider = null)
    {
        time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>搜索关键字 WHERE 组合（供查询管线复用）。</summary>
    public static IQueryable<Product> ApplyKeyword(IQueryable<Product> query, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return query;
        }

        var pattern = $"%{EscapeLike(keyword.Trim())}%";
        var escape = Escape();
        return query.Where(product =>
            EF.Functions.Like(product.Model, pattern, escape)
            || EF.Functions.Like(product.Name, pattern, escape)
            || EF.Functions.Like(product.Brand, pattern, escape)
            || EF.Functions.Like(product.Category, pattern, escape)
            || (product.SubCategory != null && EF.Functions.Like(product.SubCategory, pattern, escape)));
    }

    /// <summary>搜索排序：命中优先级 Model &gt; Name &gt; Brand &gt; Category &gt; 其他，同层按型号。</summary>
    public static IQueryable<Product> OrderBySearchRank(IQueryable<Product> query, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return query;
        }

        var pattern = $"%{EscapeLike(keyword.Trim())}%";
        var escape = Escape();
        return query
            .OrderBy(product =>
                EF.Functions.Like(product.Model, pattern, escape) ? 0
                : EF.Functions.Like(product.Name, pattern, escape) ? 1
                : EF.Functions.Like(product.Brand, pattern, escape) ? 2
                : EF.Functions.Like(product.Category, pattern, escape) ? 3
                : 4)
            .ThenBy(product => product.Model);
    }

    /// <summary>把筛选条件叠加到查询上（AND 语义，维度内 IN）。</summary>
    public IQueryable<Product> Apply(IQueryable<Product> query, ProductFilter? filter)
    {
        if (filter is null)
        {
            return query;
        }

        if (filter.Categories is { Count: > 0 } categories)
        {
            query = query.Where(product => categories.Contains(product.Category));
        }

        if (filter.Brands is { Count: > 0 } brands)
        {
            query = query.Where(product => brands.Contains(product.Brand));
        }

        if (filter.IsAvailable is { } isAvailable)
        {
            query = query.Where(product => product.IsAvailable == isAvailable);
        }

        if (filter.HasImage is { } hasImage)
        {
            query = hasImage
                ? query.Where(product => product.Sha256 != null)
                : query.Where(product => product.Sha256 == null);
        }

        if (filter.UpdateTime is { } range and not DateRange.All)
        {
            var cutoffUnix = range switch
            {
                DateRange.Last7Days => time.GetUtcNow().AddDays(-7).ToUnixTimeSeconds(),
                DateRange.Last30Days => time.GetUtcNow().AddDays(-30).ToUnixTimeSeconds(),
                _ => 0,
            };
            query = query.Where(product => product.UpdateTimeUnix != null && product.UpdateTimeUnix >= cutoffUnix);
        }

        return query;
    }

    /// <summary>
    /// 收藏维度：需要跨表子查询，由组合查询服务在同一上下文内翻译（同 ApplyUsage 先例）。
    /// </summary>
    public static IQueryable<Product> ApplyFavorite(
        IQueryable<Product> query,
        GalleryDbContext context,
        bool? isFavorite)
    {
        if (isFavorite is null)
        {
            return query;
        }

        return isFavorite.Value
            ? query.Where(product => context.Favorites.Any(row => row.ProductId == product.Id))
            : query.Where(product => !context.Favorites.Any(row => row.ProductId == product.Id));
    }

    /// <summary>
    /// 收藏夹维度：产品在指定合集内（跨表子查询，同 ApplyFavorite 先例）。
    /// </summary>
    public static IQueryable<Product> ApplyCollection(
        IQueryable<Product> query,
        GalleryDbContext context,
        int? collectionId)
    {
        if (collectionId is null)
        {
            return query;
        }

        return query.Where(product => context.CollectionItems.Any(
            row => row.CollectionId == collectionId && row.ProductId == product.Id));
    }

    /// <summary>
    /// 使用次数维度：需要跨表子查询，由组合查询服务在同一上下文内翻译（EF 不支持跨上下文实例组合）。
    /// </summary>
    public static IQueryable<Product> ApplyUsage(
        IQueryable<Product> query,
        GalleryDbContext context,
        UsageRange? usage)
    {
        if (usage is null or UsageRange.None)
        {
            return query;
        }

        return usage switch
        {
            UsageRange.NeverUsed => query.Where(product =>
                !context.ProductUsages.Any(row => row.ProductId == product.Id)),
            UsageRange.Used => query.Where(product =>
                context.ProductUsages.Any(row => row.ProductId == product.Id)),
            UsageRange.HighUsage => query.Where(product =>
                context.ProductUsages.Any(row => row.ProductId == product.Id && row.TotalUseCount >= 10)),
            _ => query,
        };
    }

    /// <summary>转义 LIKE 通配符，用户输入中的 %、_、\ 一律按字面匹配。</summary>
    private static string EscapeLike(string input)
    {
        return input
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private static string Escape()
    {
        return "\\";
    }
}

/// <summary>组合查询服务：搜索 ∩ 筛选，默认按型号排序；全部在数据库侧执行。</summary>
public sealed class ProductQueryService(
    IDbContextFactory<GalleryDbContext> contextFactory,
    IFilterService filterService,
    ISortService sortService) : IProductQueryService
{
    public async Task<IReadOnlyList<Product>> QueryAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (query.Mode == BrowseMode.Random)
        {
            return await QueryRandomAsync(query, context, cancellationToken);
        }

        var ordered = ComposeOrderedQuery(context, query);
        return await ordered.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 分页执行组合查询：COUNT 与 Skip/Take 均翻译为数据库侧 SQL，
    /// 仅当前页行被物化，内存占用与总数据量无关。随机模式无页码语义，退化为批量查询。
    /// </summary>
    public async Task<ProductPageResult> QueryPageAsync(ProductQuery query, int skip, int take, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 4096);
        skip = Math.Max(0, skip);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (query.Mode == BrowseMode.Random)
        {
            var items = await QueryRandomAsync(query, context, cancellationToken);
            return new ProductPageResult(items, items.Count);
        }

        var ordered = ComposeOrderedQuery(context, query);
        var totalCount = await ordered.CountAsync(cancellationToken);
        var rows = await ordered.Skip(skip).Take(take).ToListAsync(cancellationToken);
        return new ProductPageResult(rows, totalCount);
    }

    /// <summary>组合查询管线：关键字 ∩ 筛选 ∩ 使用 ∩ 收藏 ∩ 排序（数据库侧）。</summary>
    private IQueryable<Product> ComposeOrderedQuery(GalleryDbContext context, ProductQuery query)
    {
        var source = context.Products.AsNoTracking();
        var withKeyword = FilterService.ApplyKeyword(source, query.Keyword);
        var withFilter = filterService.Apply(withKeyword, query.Filter);
        var withUsage = FilterService.ApplyUsage(withFilter, context, query.Filter?.Usage);
        var withFavorite = FilterService.ApplyFavorite(withUsage, context, query.Filter?.IsFavorite);
        var withCollection = FilterService.ApplyCollection(withFavorite, context, query.CollectionId);
        return query.Sort switch
        {
            { Field: ProductSortField.UsageCount } usageSort =>
                SortService.ApplyUsageCountOrder(withCollection, context, usageSort.Direction == SortDirection.Descending),
            { Field: ProductSortField.LastUsed } lastUsedSort =>
                SortService.ApplyLastUsedOrder(withCollection, context, lastUsedSort.Direction == SortDirection.Descending),
            { } explicitSort => sortService.Apply(withCollection, explicitSort),
            _ => string.IsNullOrWhiteSpace(query.Keyword)
                ? withCollection.OrderBy(product => product.Model)
                : FilterService.OrderBySearchRank(withCollection, query.Keyword),
        };
    }

    /// <summary>
    /// 随机浏览查询：关键字/筛选/使用维度照常生效，按 RandomKey 游标抽取一批；
    /// 不足时从头补齐（排除本会话已展示型号），返回批次按 RandomKey 升序。
    /// </summary>
    private async Task<IReadOnlyList<Product>> QueryRandomAsync(
        ProductQuery query,
        GalleryDbContext context,
        CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, query.RandomLimit);
        var baseQuery = BuildRandomBaseQuery(context, query.Keyword, query.Filter, query.Filter?.Usage);

        var batch = await TakeRandomBatchAsync(baseQuery, query.RandomCursor, query.ExcludeModels, limit, cancellationToken);
        if (batch.Count >= limit || query.RandomCursor is null)
        {
            return batch;
        }

        // 游标之后不足：从头补齐（合并排除本会话已展示与本批已取型号）。
        var excludeAll = new HashSet<string>(StringComparer.Ordinal);
        if (query.ExcludeModels is not null)
        {
            foreach (var model in query.ExcludeModels)
            {
                excludeAll.Add(model);
            }
        }

        foreach (var product in batch)
        {
            excludeAll.Add(product.Model);
        }

        var wrapped = await TakeRandomBatchAsync(baseQuery, null, [.. excludeAll], limit - batch.Count, cancellationToken);
        if (wrapped.Count == 0)
        {
            return batch;
        }

        var merged = new List<Product>(batch.Count + wrapped.Count);
        merged.AddRange(batch);
        merged.AddRange(wrapped);
        return merged;
    }

    private IQueryable<Product> BuildRandomBaseQuery(
        GalleryDbContext context,
        string? keyword,
        ProductFilter? filter,
        UsageRange? usage)
    {
        var withKeyword = FilterService.ApplyKeyword(context.Products.AsNoTracking(), keyword);
        var withFilter = filterService.Apply(withKeyword, filter);
        return FilterService.ApplyUsage(withFilter, context, usage);
    }

    private static async Task<List<Product>> TakeRandomBatchAsync(
        IQueryable<Product> baseQuery,
        long? cursor,
        IReadOnlyList<string>? excludeModels,
        int limit,
        CancellationToken cancellationToken)
    {
        var randomReady = baseQuery.Where(product => product.RandomKey != null);
        if (cursor is { } cursorValue)
        {
            randomReady = randomReady.Where(product => product.RandomKey >= cursorValue);
        }

        if (excludeModels is { Count: > 0 } exclude)
        {
            randomReady = randomReady.Where(product => !exclude.Contains(product.Model));
        }

        return await randomReady
            .OrderBy(product => product.RandomKey)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
