using MijiaProductGallery.Core.Enums;

namespace MijiaProductGallery.Core.Models;

/// <summary>同步状态（当前快照，全表仅一行 Id=1）。</summary>
public sealed record SyncState
{
    /// <summary>恒为 1。</summary>
    public long Id { get; init; } = 1;

    /// <summary>最近一次尝试同步的时间（Unix 秒）；从未同步为 null。</summary>
    public long? LastSyncUnix { get; init; }

    /// <summary>最近一次成功同步的时间（Unix 秒）；从未成功为 null。</summary>
    public long? LastSuccessfulSyncUnix { get; init; }

    public SyncStatus Status { get; init; } = SyncStatus.Idle;

    public SyncStage Stage { get; init; } = SyncStage.NotStarted;

    public string? ErrorMessage { get; init; }

    /// <summary>本地数据对应的快照日期（yyyy-MM-dd）。</summary>
    public string? SnapshotDate { get; init; }
}
