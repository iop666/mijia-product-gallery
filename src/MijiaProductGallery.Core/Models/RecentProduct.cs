using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Models;

/// <summary>最近使用视图行：产品 + 该产品最近一次行为时间与类型。</summary>
public sealed record RecentProduct
{
    public required Product Product { get; init; }

    /// <summary>最近一次行为时间（Unix 秒）。</summary>
    public required long LastUsedUnix { get; init; }

    /// <summary>最近一次行为类型。</summary>
    public required UsageType LastEvent { get; init; }
}
