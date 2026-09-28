namespace MijiaProductGallery.Core.Models;

/// <summary>官网产品快照：对比规则的官网侧输入（已标准化、已解析归属大类）。</summary>
public sealed record RemoteProductState
{
    public required string Model { get; init; }

    public required string Name { get; init; }

    public required string Brand { get; init; }

    /// <summary>归属大类名（按官网分类列表成员关系解析，与产品对象自带 ptId 无关）。</summary>
    public required string Category { get; init; }

    /// <summary>官网图片 URL。</summary>
    public required string RealIconUrl { get; init; }

    public long CreateTimeUnix { get; init; }

    public long UpdateTimeUnix { get; init; }
}
