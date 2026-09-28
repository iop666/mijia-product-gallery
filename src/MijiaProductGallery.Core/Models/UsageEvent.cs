using MijiaProductGallery.Core.Enums;

namespace MijiaProductGallery.Core.Models;

/// <summary>单次使用行为记录（用户数据），用于最近使用页。</summary>
public sealed record UsageEvent
{
    public long Id { get; init; }

    public required int ProductId { get; init; }

    public required UsageType Type { get; init; }

    public required long UsedUnix { get; init; }
}
