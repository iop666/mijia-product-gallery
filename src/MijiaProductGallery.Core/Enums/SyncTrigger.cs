namespace MijiaProductGallery.Core.Enums;

/// <summary>同步触发方式。</summary>
public enum SyncTrigger
{
    /// <summary>用户在同步中心手动触发。</summary>
    Manual,

    /// <summary>应用启动时自动检查触发。</summary>
    Startup,

    /// <summary>按同步周期计划触发。</summary>
    Scheduled,
}
