using MijiaProductGallery.Core.Enums;

namespace MijiaProductGallery.Core.Models;

/// <summary>单个型号的对比结论。Type 为最高优先级变更；其余字段记录同轮发生的全部差异。</summary>
public sealed record ProductChange
{
    public required ChangeType Type { get; init; }

    public required string Model { get; init; }

    public string? OldCategory { get; init; }

    public string? NewCategory { get; init; }

    public NameChangeKind NameChange { get; init; } = NameChangeKind.None;

    public string? OldName { get; init; }

    public string? NewName { get; init; }

    public string? OldBrand { get; init; }

    public string? NewBrand { get; init; }

    public ImageOutcome ImageOutcome { get; init; } = ImageOutcome.None;

    /// <summary>受影响的现用图片文件名；IdReused 时即新图将占用的原文件名。</summary>
    public string? ImageFileName { get; init; }

    /// <summary>IdReused 时旧图的去向文件名（.old 链链尾）。</summary>
    public string? ReplacedByOldFileName { get; init; }

    /// <summary>官网当前图片 URL。</summary>
    public string? ImageUrl { get; init; }
}
