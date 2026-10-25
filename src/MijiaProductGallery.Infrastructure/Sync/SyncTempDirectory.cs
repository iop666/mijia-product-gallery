using Microsoft.Extensions.Logging;

namespace MijiaProductGallery.Infrastructure.Sync;

/// <summary>
/// 图片复核临时目录：每轮同步在 %TEMP%\MijiaProductGallery 下新建 sync-&lt;guid&gt;，
/// 运行期间对锁文件持有独占句柄；进程被强杀后句柄随进程释放，下次启动即可识别并回收。
/// </summary>
public static class SyncTempDirectory
{
    private const string RootFolderName = "MijiaProductGallery";
    private const string DirectoryPrefix = "sync-";
    private const string LockFileName = ".sync.lock";
    private const string CleanupLogFileName = "cleanup.log";

    /// <summary>无锁目录（升级前版本创建）超过该时长无写入才视为残留，避免误删仍在运行的旧实例。</summary>
    private static readonly TimeSpan LegacyStaleAge = TimeSpan.FromMinutes(30);

    public static string RootDirectory => Path.Combine(Path.GetTempPath(), RootFolderName);

    /// <summary>创建本轮目录并锁定；锁句柄由调用方持有到临时文件不再使用为止。</summary>
    internal static (string Path, FileStream Lock) Create()
    {
        var path = Path.Combine(RootDirectory, $"{DirectoryPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        var lockStream = new FileStream(
            Path.Combine(path, LockFileName),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);
        return (path, lockStream);
    }

    /// <summary>删除目录；失败不抛出，留给下次启动清扫。</summary>
    public static void Delete(string path, ILogger? logger = null)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Warn("临时同步目录删除失败，等待下次启动重试", path, exception, logger);
        }
    }

    /// <summary>
    /// 回收无进程持有的 sync-* 目录（进程被强杀/断电，或升级前版本遗留）。
    /// 锁文件能被独占打开说明上一轮已退出；无锁目录按最后写入时间超过阈值再判定为残留。
    /// </summary>
    public static int CleanupOrphans(string? rootDirectory = null, ILogger? logger = null)
    {
        var root = rootDirectory ?? RootDirectory;
        string[] directories;
        try
        {
            if (!Directory.Exists(root))
            {
                return 0;
            }

            directories = Directory.GetDirectories(root, $"{DirectoryPrefix}*");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Warn("扫描临时同步目录失败", root, exception, logger);
            return 0;
        }

        var removed = 0;
        foreach (var path in directories)
        {
            try
            {
                if (!CanDelete(path))
                {
                    continue;
                }

                Delete(path, logger);
                if (!Directory.Exists(path))
                {
                    removed++;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Warn("回收临时同步目录失败", path, exception, logger);
            }
        }

        return removed;
    }

    /// <summary>应用未注册日志提供程序时兜底写入临时根，避免删除失败无迹可查。</summary>
    private static void Warn(string message, string path, Exception exception, ILogger? logger)
    {
        if (logger is not null)
        {
            logger.LogWarning(exception, "{Message}：{Path}", message, path);
            return;
        }

        try
        {
            Directory.CreateDirectory(RootDirectory);
            File.AppendAllText(
                Path.Combine(RootDirectory, CleanupLogFileName),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}：{path}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static bool CanDelete(string path)
    {
        var lockPath = Path.Combine(path, LockFileName);
        if (!File.Exists(lockPath))
        {
            return IsLegacyStale(path);
        }

        try
        {
            using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsLegacyStale(string path)
    {
        try
        {
            var newest = Directory.GetLastWriteTimeUtc(path);
            if (DateTime.UtcNow - newest <= LegacyStaleAge)
            {
                return false; // 目录仍有新文件写入，可能属于未升级的旧实例。
            }

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var writeTime = File.GetLastWriteTimeUtc(file);
                if (writeTime > newest)
                {
                    newest = writeTime;
                }
            }

            return DateTime.UtcNow - newest > LegacyStaleAge;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
