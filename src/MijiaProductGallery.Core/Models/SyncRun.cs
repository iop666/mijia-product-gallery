using MijiaProductGallery.Core.Enums;

namespace MijiaProductGallery.Core.Models;

/// <summary>一轮同步的执行记录。</summary>
public sealed record SyncRun
{
    public long Id { get; init; }

    public required long StartedUnix { get; init; }

    public long? EndedUnix { get; init; }

    public SyncStatus Status { get; init; } = SyncStatus.Running;

    public SyncStage Stage { get; init; } = SyncStage.NotStarted;

    public SyncTrigger Trigger { get; init; } = SyncTrigger.Manual;

    public SyncRunCounts? Counts { get; init; }

    public string? ErrorMessage { get; init; }
}

/// <summary>一轮同步的变更统计。</summary>
public sealed record SyncRunCounts
{
    public int NewCount { get; init; }

    public int DelistedCount { get; init; }

    public int CategoryChangedCount { get; init; }

    public int NameChangedCount { get; init; }

    public int ImageChangedCount { get; init; }

    public int IdReusedCount { get; init; }

    public int ImageFailureCount { get; init; }
}
