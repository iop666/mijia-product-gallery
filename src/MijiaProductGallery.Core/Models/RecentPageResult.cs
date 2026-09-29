namespace MijiaProductGallery.Core.Models;

/// <summary>最近使用分页结果：当前页行 + 聚合总产品数（数据库侧 COUNT(DISTINCT)）。</summary>
public sealed record RecentPageResult(IReadOnlyList<RecentProduct> Items, int TotalCount)
{
    /// <summary>按每页行数计算总页数（至少 1 页，空结果也为 1 页以便页码展示）。</summary>
    public int TotalPages(int pageSize)
    {
        return pageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)pageSize));
    }
}
