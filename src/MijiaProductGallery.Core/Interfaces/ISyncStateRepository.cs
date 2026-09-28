using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>同步状态与同步留痕仓储（仅同步引擎使用）。</summary>
public interface ISyncStateRepository
{
    /// <summary>读取当前状态；从未初始化时返回 Id=1 的 Idle 默认状态。</summary>
    Task<SyncState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>保存当前状态（始终写入 Id=1 单行）。</summary>
    Task SaveStateAsync(SyncState state, CancellationToken cancellationToken = default);

    /// <summary>开启一轮同步记录，返回运行 Id。</summary>
    Task<long> StartRunAsync(SyncTrigger trigger, long startedUnix, CancellationToken cancellationToken = default);

    Task CompleteRunAsync(
        long runId,
        SyncStatus status,
        SyncStage stage,
        SyncRunCounts? counts,
        string? errorMessage,
        long endedUnix,
        CancellationToken cancellationToken = default);

    /// <summary>按 Id 取同步运行记录。</summary>
    Task<SyncRun?> GetRunAsync(long runId, CancellationToken cancellationToken = default);

    /// <summary>把一轮对比结论写入 SyncChanges（含 JSON 明细）。</summary>
    Task AddChangesAsync(long runId, IReadOnlyList<ProductChange> changes, CancellationToken cancellationToken = default);
}
