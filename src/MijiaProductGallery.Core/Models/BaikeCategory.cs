namespace MijiaProductGallery.Core.Models;

/// <summary>米家百科分类（productCategories 接口 data.list 元素的字段子集）。</summary>
public sealed record BaikeCategory
{
    /// <summary>分类 ptId；-10000 为「新上线」滚动上新货架。</summary>
    public required int PtId { get; init; }

    public required string Name { get; init; }
}
