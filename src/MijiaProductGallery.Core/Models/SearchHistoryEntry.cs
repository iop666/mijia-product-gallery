namespace MijiaProductGallery.Core.Models;

/// <summary>搜索历史条目（用户数据）。</summary>
public sealed record SearchHistoryEntry
{
    public long Id { get; init; }

    public required string Query { get; init; }

    public required long CreatedUnix { get; init; }

    /// <summary>该次搜索命中的产品数。</summary>
    public int ResultCount { get; init; }
}
