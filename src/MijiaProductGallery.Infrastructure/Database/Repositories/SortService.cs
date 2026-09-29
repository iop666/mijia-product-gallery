using System.Linq.Expressions;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;
using MijiaProductGallery.Infrastructure.Database;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>
/// 排序条件翻译：ProductSort → IQueryable 组合。
/// 使用次数/最近使用维度需要跨表子查询，由组合查询服务在同一上下文内翻译（见 ApplyUsageOrder）；
/// 排序一律追加型号次序保证同值稳定。
/// </summary>
public sealed class SortService : ISortService
{
    /// <summary>把排序叠加到查询上；sort 为 null 表示未启用显式排序。</summary>
    public IQueryable<Product> Apply(IQueryable<Product> query, ProductSort? sort)
    {
        if (sort is null)
        {
            return query;
        }

        var descending = sort.Direction == SortDirection.Descending;
        return sort.Field switch
        {
            ProductSortField.Model => Ord(query, product => product.Model, descending),
            ProductSortField.Name => Ord(query, product => product.Name, descending),
            ProductSortField.UpdateTime => Ord(query, product => product.UpdateTimeUnix ?? 0, descending),
            ProductSortField.AddedTime => Ord(query, product => product.FirstSeenUnix, descending),
            // LastUsed 需跨表子查询，由 ProductQueryService 路由到 ApplyLastUsedOrder。
            _ => query,
        };
    }

    /// <summary>
    /// 使用次数排序：按 ProductUsages.TotalUseCount（无记录 COALESCE 为 0），
    /// 需要跨表子查询，由组合查询服务在同一上下文内调用。
    /// </summary>
    public static IOrderedQueryable<Product> ApplyUsageCountOrder(
        IQueryable<Product> query,
        GalleryDbContext context,
        bool descending)
    {
        IOrderedQueryable<Product> ordered = descending
            ? query.OrderByDescending(product =>
                context.ProductUsages
                    .Where(row => row.ProductId == product.Id)
                    .Select(row => (long?)row.TotalUseCount)
                    .FirstOrDefault() ?? 0)
            : query.OrderBy(product =>
                context.ProductUsages
                    .Where(row => row.ProductId == product.Id)
                    .Select(row => (long?)row.TotalUseCount)
                    .FirstOrDefault() ?? 0);
        return ordered.ThenBy(product => product.Model);
    }

    /// <summary>
    /// 最近使用排序：按 ProductUsages.LastUsedUnix（无记录视为最低，升序时排最前），
    /// 需要跨表子查询，由组合查询服务在同一上下文内调用。
    /// </summary>
    public static IOrderedQueryable<Product> ApplyLastUsedOrder(
        IQueryable<Product> query,
        GalleryDbContext context,
        bool descending)
    {
        IOrderedQueryable<Product> ordered = descending
            ? query.OrderByDescending(product =>
                context.ProductUsages
                    .Where(row => row.ProductId == product.Id)
                    .Select(row => (long?)row.LastUsedUnix)
                    .FirstOrDefault() ?? long.MinValue)
            : query.OrderBy(product =>
                context.ProductUsages
                    .Where(row => row.ProductId == product.Id)
                    .Select(row => (long?)row.LastUsedUnix)
                    .FirstOrDefault() ?? long.MinValue);
        return ordered.ThenBy(product => product.Model);
    }

    private static IOrderedQueryable<Product> Ord<TKey>(
        IQueryable<Product> query,
        Expression<Func<Product, TKey>> keySelector,
        bool descending)
    {
        IOrderedQueryable<Product> ordered = descending
            ? query.OrderByDescending(keySelector)
            : query.OrderBy(keySelector);
        return ordered.ThenBy(product => product.Model);
    }
}
