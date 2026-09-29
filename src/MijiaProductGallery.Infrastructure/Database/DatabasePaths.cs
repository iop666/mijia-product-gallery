using MijiaProductGallery.Core.Interfaces;

namespace MijiaProductGallery.Infrastructure.Database;

/// <summary>
/// 图库数据目录布局：数据库、原图、缩略图、备份、日志、缓存、状态、种子包，
/// 全部派生自唯一数据根。默认根为"应用目录\Data"（安装版=安装目录，便携版=解压目录），
/// 可注入替代根（测试）。历史数据位于 %LOCALAPPDATA%\MijiaProductGallery，
/// 由 DataRootMigrator 在首次启动时迁移至新根。
/// </summary>
public sealed class DatabasePaths : IAppDataRoot
{
    public DatabasePaths(string? rootOverride = null)
    {
        Root = rootOverride
            ?? Path.Combine(AppContext.BaseDirectory, "Data");
    }

    public string Root { get; }

    /// <summary>接口视图的数据根（与 Root 相同）。</summary>
    string Core.Interfaces.IAppDataRoot.RootPath => Root;

    public string DatabaseDirectory => Path.Combine(Root, "Database");

    public string DatabaseFile => Path.Combine(DatabaseDirectory, "gallery.db");

    public string ImagesDirectory => Path.Combine(Root, "Images");

    public string ThumbnailsDirectory => Path.Combine(Root, "Thumbnails");

    public string BackupsDirectory => Path.Combine(Root, "Backups");

    public string LogsDirectory => Path.Combine(Root, "Logs");

    public string CacheDirectory => Path.Combine(Root, "Cache");

    public string StateDirectory => Path.Combine(Root, "State");

    /// <summary>种子包约定存放目录（首次初始化自动查找位置之一）。</summary>
    public string SeedDirectory => Path.Combine(Root, "Seed");

    /// <summary>创建全部目录（幂等）。</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DatabaseDirectory);
        Directory.CreateDirectory(ImagesDirectory);
        Directory.CreateDirectory(ThumbnailsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(SeedDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(StateDirectory);
    }
}
