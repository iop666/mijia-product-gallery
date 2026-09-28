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

/// <summary>组合查询服务：搜索关键字 ∩ 筛选条件，默认按型号排序。</summary>
public interface IProductQueryService
{
    /// <summary>执行组合查询（数据库侧过滤与排序）。</summary>
    Task<IReadOnlyList<Product>> QueryAsync(ProductQuery query, CancellationToken cancellationToken = default);
}
