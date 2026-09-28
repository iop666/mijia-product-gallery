namespace MijiaProductGallery.Core.Query;

/// <summary>使用次数筛选范围。</summary>
public enum UsageRange
{
    /// <summary>不筛选使用次数。</summary>
    None,

    /// <summary>从未使用（TotalUseCount == 0）。</summary>
    NeverUsed,

    /// <summary>已使用（TotalUseCount &gt; 0）。</summary>
    Used,

    /// <summary>高频使用（TotalUseCount &gt;= 10）。</summary>
    HighUsage,
}

/// <summary>官网更新时间筛选范围（相对当前时间）。</summary>
public enum DateRange
{
    /// <summary>全部时间。</summary>
    All,

    /// <summary>最近 7 天。</summary>
    Last7Days,

    /// <summary>最近 30 天。</summary>
    Last30Days,
}

/// <summary>
/// 产品筛选条件（用户视图状态，非官方数据）：各维度为 AND 关系，维度内取值为 OR（IN）。
/// 任何维度为 null / 空 / None / All 表示该维度不筛选。
/// </summary>
public sealed class ProductFilter
{
    public IReadOnlyList<string>? Categories { get; set; }

    public IReadOnlyList<string>? Brands { get; set; }

    /// <summary>true=仅在架；false=仅已下架；null=全部。</summary>
    public bool? IsAvailable { get; set; }

    /// <summary>true=仅有图片；false=仅无图片；null=全部。</summary>
    public bool? HasImage { get; set; }

    public UsageRange? Usage { get; set; }

    public DateRange? UpdateTime { get; set; }

    /// <summary>是否所有维度都未启用筛选。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty =>
        (Categories is null || Categories.Count == 0)
        && (Brands is null || Brands.Count == 0)
        && IsAvailable is null
        && HasImage is null
        && (Usage is null or UsageRange.None)
        && (UpdateTime is null or DateRange.All);

    /// <summary>当前筛选的浅拷贝（集合复制）。</summary>
    public ProductFilter Clone()
    {
        return new ProductFilter
        {
            Categories = Categories is null ? null : [.. Categories],
            Brands = Brands is null ? null : [.. Brands],
            IsAvailable = IsAvailable,
            HasImage = HasImage,
            Usage = Usage,
            UpdateTime = UpdateTime,
        };
    }
}

/// <summary>图库查询：搜索关键字与筛选条件的组合（交集语义）。</summary>
public sealed class ProductQuery
{
    /// <summary>搜索关键字；空表示不过滤关键字。</summary>
    public string? Keyword { get; init; }

    public ProductFilter? Filter { get; init; }
}
