using MijiaProductGallery.Core.Enums;

namespace MijiaProductGallery.Core.Models;

/// <summary>使用统计快照（今日/本周/本月/累计 + 最常使用产品）。</summary>
public sealed record UsageStatistics
{
    /// <summary>今日使用次数（本地自然日，含全部行为类型）。</summary>
    public required long TodayCount { get; init; }

    /// <summary>本周使用次数（本地自然周，周一起算）。</summary>
    public required long WeekCount { get; init; }

    /// <summary>本月使用次数（本地自然月）。</summary>
    public required long MonthCount { get; init; }

    /// <summary>累计使用次数（ProductUsages.TotalUseCount 汇总）。</summary>
    public required long TotalCount { get; init; }

    /// <summary>最常使用产品（按累计次数降序，最多 topCount 条，不含零次产品）。</summary>
    public required IReadOnlyList<TopUsedProduct> TopProducts { get; init; }
}

/// <summary>最常使用产品条目。</summary>
public sealed record TopUsedProduct
{
    public required int ProductId { get; init; }

    public required string Model { get; init; }

    public required string Name { get; init; }

    public required int TotalUseCount { get; init; }
}
