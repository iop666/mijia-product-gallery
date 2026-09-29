namespace MijiaProductGallery.Core.Interfaces;

/// <summary>
/// 应用数据根：全部运行数据（数据库/原图/缩略图/备份/日志/缓存/状态/种子包）
/// 的唯一路径派生点。安装版与便携版统一为"应用目录\Data"，
/// 禁止使用 %LOCALAPPDATA%、%APPDATA%、%PROGRAMDATA% 等系统用户目录。
/// </summary>
public interface IAppDataRoot
{
    /// <summary>数据根目录（应用目录\Data）。</summary>
    string RootPath { get; }

    string DatabaseDirectory { get; }

    string DatabaseFile { get; }

    string ImagesDirectory { get; }

    string ThumbnailsDirectory { get; }

    string BackupsDirectory { get; }

    string LogsDirectory { get; }

    string CacheDirectory { get; }

    string StateDirectory { get; }

    /// <summary>种子包约定存放目录（首次初始化自动查找位置之一）。</summary>
    string SeedDirectory { get; }

    /// <summary>创建全部目录（幂等）。</summary>
    void EnsureDirectories();
}
