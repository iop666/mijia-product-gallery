namespace MijiaProductGallery.Core.Models;

/// <summary>图库数据概览（产品维度只读统计；大类合计恒等于产品总数）。</summary>
public sealed record GalleryOverview
{
    /// <summary>产品总数。</summary>
    public required int TotalCount { get; init; }

    /// <summary>有图片文件的产品数（Sha256 与 ImagePath 齐备）。</summary>
    public required int WithImageCount { get; init; }

    /// <summary>无图片的产品数。</summary>
    public required int WithoutImageCount { get; init; }

    /// <summary>官网已移除的产品数（IsAvailable=false，仍计入产品总数）。</summary>
    public required int RemovedCount { get; init; }

    /// <summary>各大类数量（按数量降序）。</summary>
    public required IReadOnlyList<CategoryCount> Categories { get; init; }
}

/// <summary>大类条目。</summary>
public sealed record CategoryCount
{
    public required string Category { get; init; }

    public required int Count { get; init; }
}
