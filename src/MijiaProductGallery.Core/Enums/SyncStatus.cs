namespace MijiaProductGallery.Core.Enums;

/// <summary>同步任务的整体状态。</summary>
public enum SyncStatus
{
    /// <summary>空闲，从未同步或上一轮已结束。</summary>
    Idle,

    /// <summary>同步进行中。</summary>
    Running,

    /// <summary>最近一次同步成功。</summary>
    Success,

    /// <summary>最近一次同步失败。</summary>
    Failed,
}
