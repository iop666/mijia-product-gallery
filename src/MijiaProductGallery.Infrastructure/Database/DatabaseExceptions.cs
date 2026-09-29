namespace MijiaProductGallery.Infrastructure.Database;

/// <summary>数据库文件完整性校验失败。库文件保持原样，由用户选择恢复路径。</summary>
public sealed class GalleryDatabaseCorruptException(string detail)
    : InvalidOperationException($"本地数据库完整性校验失败，已保持原文件未做任何改动。{detail}")
{
    /// <summary>quick_check 返回的问题描述。</summary>
    public string Detail { get; } = detail;
}

/// <summary>数据库由更新版本的程序创建（降级场景）。拒绝打开，不做任何修改。</summary>
public sealed class GalleryDatabaseNewerThanAppException(string detail)
    : InvalidOperationException($"本地数据库由更新版本的应用创建，请升级应用或从备份恢复。{detail}")
{
    public string Detail { get; } = detail;
}
