using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>
/// 产品搜索契约（实现于数据层：SQLite LIKE + 索引，未来可替换 FTS5 而不改调用方）。
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// 七字段模糊搜索：Model / Name / Brand / Category / SubCategory（Description、Alias
    /// 为预留字段，当前无对应列）。空输或纯空白返回空结果。
    /// 过滤与排序全部在数据库侧完成；排序优先级 Model &gt; Name &gt; Brand &gt; Category &gt; 其他。
    /// </summary>
    Task<IReadOnlyList<Product>> SearchAsync(string keyword, CancellationToken cancellationToken = default);
}
