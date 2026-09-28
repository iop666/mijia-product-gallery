namespace MijiaProductGallery.Core.Query;

/// <summary>排序字段。</summary>
public enum ProductSortField
{
    /// <summary>型号（默认排序）。</summary>
    Model,

    /// <summary>产品名称。</summary>
    Name,

    /// <summary>使用次数（TotalUseCount，无记录按 0）。</summary>
    UsageCount,

    /// <summary>最近使用（LastUsedUnix，无记录视为最低）。</summary>
    LastUsed,

    /// <summary>官网更新时间。</summary>
    UpdateTime,

    /// <summary>本地收录时间（FirstSeenUnix）。</summary>
    AddedTime,
}

/// <summary>排序方向。</summary>
public enum SortDirection
{
    Ascending,
    Descending,
}

/// <summary>
/// 排序条件（用户视图状态）。null 表示未启用显式排序：
/// 有关键字时按搜索命中优先级，无关键字时按型号升序。
/// </summary>
public sealed class ProductSort
{
    public ProductSortField Field { get; set; } = ProductSortField.Model;

    public SortDirection Direction { get; set; } = SortDirection.Ascending;
}
