namespace MijiaProductGallery.Core.Models;

/// <summary>同步产生的单条变更留痕（含判定依据，可追溯）。</summary>
public sealed record SyncChange
{
    public long Id { get; init; }

    public required long SyncRunId { get; init; }

    public required ProductChange Change { get; init; }
}
