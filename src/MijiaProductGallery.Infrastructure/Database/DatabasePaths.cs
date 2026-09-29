namespace MijiaProductGallery.Infrastructure.Database;

/// <summary>
/// 图库数据目录布局：数据库、原图、缩略图、备份、日志五个子目录，均位于同一数据根下。
/// 默认数据根为 %LOCALAPPDATA%\MijiaProductGallery，可注入替代根（测试/便携模式）。
/// </summary>
public sealed class DatabasePaths
{
    public DatabasePaths(string? rootOverride = null)
    {
        Root = rootOverride
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MijiaProductGallery");
    }

    public string Root { get; }

    public string DatabaseDirectory => Path.Combine(Root, "database");

    public string DatabaseFile => Path.Combine(DatabaseDirectory, "gallery.db");

    public string ImagesDirectory => Path.Combine(Root, "images");

    public string ThumbnailsDirectory => Path.Combine(Root, "thumbnails");

    public string BackupsDirectory => Path.Combine(Root, "backups");

    /// <summary>种子包约定存放目录（首次初始化自动查找位置之一）。</summary>
    public string SeedDirectory => Path.Combine(Root, "seed");

    public string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>创建全部目录（幂等）。</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DatabaseDirectory);
        Directory.CreateDirectory(ImagesDirectory);
        Directory.CreateDirectory(ThumbnailsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(SeedDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
