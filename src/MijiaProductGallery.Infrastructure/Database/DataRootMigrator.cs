namespace MijiaProductGallery.Infrastructure.Database;

/// <summary>迁移结果状态。</summary>
public enum DataRootMigrationStatus
{
    /// <summary>无历史数据目录（全新安装）。</summary>
    NoLegacy,

    /// <summary>新根已存在数据库（此前已迁移或已在应用目录运行），跳过。</summary>
    AlreadyMigrated,

    /// <summary>全部子目录迁移成功，旧数据根已清理。</summary>
    Migrated,

    /// <summary>部分成功：个别目录移动失败（如文件被占用），旧数据保留，未覆盖任何新数据。</summary>
    Partial,
}

/// <summary>迁移结果明细。</summary>
public sealed record DataRootMigrationResult
{
    public required DataRootMigrationStatus Status { get; init; }

    /// <summary>成功移动的子目录名。</summary>
    public required IReadOnlyList<string> MovedDirectories { get; init; }

    /// <summary>因冲突被跳过的条目（不覆盖原则）。</summary>
    public required IReadOnlyList<string> SkippedEntries { get; init; }

    /// <summary>迁移日志文件路径（位于新根 Logs 下）；无日志时为 null。</summary>
    public string? LogPath { get; init; }
}

/// <summary>
/// 历史数据根（%LOCALAPPDATA%\MijiaProductGallery）→ 新数据根（应用目录\Data）的一次性迁移器。
/// 幂等：新根已存在数据库即跳过；冲突不覆盖；全部成功后清理旧数据根，任何失败保留旧数据。
/// 必须在应用创建任何数据库连接之前调用（App 构造最早期）。
/// </summary>
public static class DataRootMigrator
{
    /// <summary>历史数据根（迁移来源）。</summary>
    public static string LegacyRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MijiaProductGallery");

    /// <summary>历史子目录名（小写旧布局）。</summary>
    private static readonly string[] LegacySubDirectories =
        ["database", "images", "thumbnails", "backups", "logs", "seed"];

    public static DataRootMigrationResult Migrate(string newRoot, string? legacyRootOverride = null)
    {
        return MigrateCore(newRoot, legacyRootOverride ?? LegacyRoot);
    }

    /// <summary>迁移核心（legacyRoot 可注入以便测试）。</summary>
    private static DataRootMigrationResult MigrateCore(string newRoot, string legacyRoot)
    {
        var moved = new List<string>();
        var skipped = new List<string>();
        string? logPath = null;

        var newDatabaseFile = Path.Combine(newRoot, "Database", "gallery.db");
        if (File.Exists(newDatabaseFile))
        {
            return new DataRootMigrationResult
            {
                Status = DataRootMigrationStatus.AlreadyMigrated,
                MovedDirectories = [],
                SkippedEntries = [],
            };
        }

        if (!Directory.Exists(legacyRoot) || !Directory.EnumerateDirectories(legacyRoot).Any())
        {
            return new DataRootMigrationResult
            {
                Status = DataRootMigrationStatus.NoLegacy,
                MovedDirectories = [],
                SkippedEntries = [],
            };
        }

        var logLines = new List<string>
        {
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 数据根迁移开始：{legacyRoot} → {newRoot}",
        };

        Directory.CreateDirectory(newRoot);
        var logsDirectory = Path.Combine(newRoot, "Logs");
        Directory.CreateDirectory(logsDirectory);
        logPath = Path.Combine(logsDirectory, "migration.log");

        var anyFailure = false;
        foreach (var sub in LegacySubDirectories)
        {
            var source = Path.Combine(legacyRoot, sub);
            if (!Directory.Exists(source))
            {
                continue;
            }

            var target = Path.Combine(newRoot, Capitalize(sub));
            try
            {
                MoveDirectoryContents(source, target, skipped, logLines);
                TryDeleteEmptyDirectory(source);
                moved.Add(sub);
                logLines.Add($"已迁移 {sub} → {target}");
            }
            catch (Exception exception)
            {
                anyFailure = true;
                logLines.Add($"迁移 {sub} 失败：{exception.Message}（旧数据保留，未覆盖新数据）");
            }
        }

        var status = anyFailure ? DataRootMigrationStatus.Partial : DataRootMigrationStatus.Migrated;
        if (status == DataRootMigrationStatus.Migrated)
        {
            // 全部成功：清理旧数据根残留（空目录/杂项文件）。删除失败不影响迁移结果。
            try
            {
                Directory.Delete(legacyRoot, recursive: true);
                logLines.Add("旧数据根已清理");
            }
            catch (Exception exception)
            {
                logLines.Add($"旧数据根清理失败（保留）：{exception.Message}");
            }
        }
        else
        {
            logLines.Add("存在失败项：旧数据根完整保留，可修复后重新迁移");
        }

        logLines.Add($"{DateTime.Now:HH:mm:ss} 迁移结束：{status}");
        try
        {
            File.WriteAllLines(logPath, logLines);
        }
        catch
        {
            logPath = null;
        }

        return new DataRootMigrationResult
        {
            Status = status,
            MovedDirectories = moved,
            SkippedEntries = skipped,
            LogPath = logPath,
        };
    }

    /// <summary>把源目录全部内容移入目标目录：目标优先整目录移动（同卷原子），跨卷或部分冲突回退为逐文件移动（同名不覆盖）。</summary>
    private static void MoveDirectoryContents(string source, string target, List<string> skipped, List<string> logLines)
    {
        Directory.CreateDirectory(target);
        try
        {
            // 整目录移动要求目标不存在；目标已建空目录时退回逐文件。
            foreach (var entry in Directory.GetFileSystemEntries(source))
            {
                MoveOne(entry, Path.Combine(target, Path.GetFileName(entry)), skipped, logLines);
            }
        }
        catch (IOException)
        {
            // 逐文件移动兜底已覆盖跨卷场景。
        }
    }

    private static void MoveOne(string source, string target, List<string> skipped, List<string> logLines)
    {
        if (File.Exists(source))
        {
            if (File.Exists(target))
            {
                skipped.Add(target);
                logLines.Add($"跳过同名文件（不覆盖）：{Path.GetFileName(source)}");
                return;
            }

            File.Move(source, target);
            return;
        }

        if (Directory.Exists(source))
        {
            if (Directory.Exists(target))
            {
                foreach (var child in Directory.GetFileSystemEntries(source))
                {
                    MoveOne(child, Path.Combine(target, Path.GetFileName(child)), skipped, logLines);
                }

                TryDeleteEmptyDirectory(source);
                return;
            }

            try
            {
                Directory.Move(source, target);
            }
            catch (IOException)
            {
                // 跨卷：复制后删除。
                CopyDirectory(source, target);
                Directory.Delete(source, true);
            }
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string Capitalize(string name)
    {
        return string.IsNullOrEmpty(name)
            ? name
            : char.ToUpperInvariant(name[0]) + name[1..];
    }
}
