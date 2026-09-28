using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>
/// 筛选条件到查询表达式的映射。职责仅是把 ProductFilter 翻译为 IQueryable 组合，
/// 不访问 UI、不触碰数据库连接；时间相对范围（最近 7/30 天）以实现方注入的时钟为准。
/// </summary>
public interface IFilterService
{
    /// <summary>把筛选条件叠加到查询上（AND 语义，维度内 IN）。</summary>
    IQueryable<Product> Apply(IQueryable<Product> query, ProductFilter? filter);
}

/// <summary>
/// 排序条件翻译：ProductSort → IQueryable 组合。
/// 职责仅是把排序翻译为表达式，不访问 UI、Settings、数据库；
/// 使用次数维度需跨表子查询，由组合查询服务在同一上下文内翻译（见 SortService.ApplyUsageOrder）。
/// </summary>
public interface ISortService
{
    /// <summary>把排序叠加到查询上（追加型号次序保证同值稳定）。</summary>
    IQueryable<Product> Apply(IQueryable<Product> query, ProductSort? sort);
}

/// <summary>组合查询服务：搜索 ∩ 筛选 ∩ 排序，全部在数据库侧执行。</summary>
public interface IProductQueryService
{
    /// <summary>执行组合查询（数据库侧过滤与排序）。</summary>
    Task<IReadOnlyList<Product>> QueryAsync(ProductQuery query, CancellationToken cancellationToken = default);
}
