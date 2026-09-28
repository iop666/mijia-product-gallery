namespace MijiaProductGallery.Core.Enums;

/// <summary>自动同步频率（关闭/每次启动/6 小时/每天/每周）。</summary>
public enum SyncAutoInterval
{
    /// <summary>关闭自动同步。</summary>
    Off,

    /// <summary>每次应用启动时同步一次。</summary>
    Startup,

    /// <summary>距上次成功同步 6 小时后触发。</summary>
    Hours6,

    /// <summary>距上次成功同步 24 小时后触发。</summary>
    Daily,

    /// <summary>距上次成功同步 7 天后触发。</summary>
    Weekly,
}
