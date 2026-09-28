namespace MijiaProductGallery.Core.Interfaces;

/// <summary>产品搜索契约（由数据层实现；当前实现基于索引查询，可替换为 FTS5 而不改调用方）。</summary>
public interface ISearchService
{
    /// <summary>按关键字模糊搜索产品（名称/型号/品牌/产品 ID/大类/小类/文件名），返回按默认排序的产品 Id 列表。</summary>
    Task<IReadOnlyList<int>> FindProductIdsAsync(string query, CancellationToken cancellationToken = default);
}
