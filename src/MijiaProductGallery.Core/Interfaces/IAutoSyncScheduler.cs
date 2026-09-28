using MijiaProductGallery.Core.Enums;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>
/// 自动同步调度器契约：按 SyncAutoInterval 周期检查并触发 Scheduled 同步。
/// Startup 语义 = 仅应用启动后的首次检查触发；周期检查对 Startup 恒为不触发。
/// </summary>
public interface IAutoSyncScheduler
{
    /// <summary>启动周期检查（幂等；已启动则为无操作）。</summary>
    void Start();

    /// <summary>
    /// 执行一次到期检查：到期则触发 Scheduled 同步。
    /// isStartupCheck=true 时按 Startup 语义判定（仅本次启动检查触发）。
    /// </summary>
    Task CheckAndTriggerAsync(bool isStartupCheck = false, CancellationToken cancellationToken = default);
}
