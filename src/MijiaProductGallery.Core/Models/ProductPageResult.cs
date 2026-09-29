namespace MijiaProductGallery.Core.Models;

/// <summary>分页查询结果：当前页产品行 + 满足条件的总行数（数据库侧 COUNT）。</summary>
public sealed record ProductPageResult(IReadOnlyList<Product> Items, int TotalCount)
{
    /// <summary>按每页行数计算总页数（至少 1 页，空结果也为 1 页以便页码展示）。</summary>
    public int TotalPages(int pageSize)
    {
        return pageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)pageSize));
    }
}
