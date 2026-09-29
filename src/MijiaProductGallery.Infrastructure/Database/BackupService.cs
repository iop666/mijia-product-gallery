using Microsoft.Data.Sqlite;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Infrastructure.Database;

namespace MijiaProductGallery.Infrastructure.Database;

/// <summary>备份文件校验失败（损坏、缺表或不是 SQLite 文件）。</summary>
public sealed class BackupValidationException(string message) : InvalidOperationException(message);

/// <summary>
/// 数据库备份/恢复实现：
/// 备份 = VACUUM INTO 单文件一致性快照（含全部表与用户数据）；
/// 恢复 = quick_check + 必需表校验 + 当前库安全快照 + 原子替换（失败即回滚替换）。
/// 图片目录不在备份范围（独立目录，恢复后缺失图片按占位显示）。
/// </summary>
public sealed class BackupService(DatabasePaths paths) : IBackupService
{
    private static readonly string[] RequiredTables =
    [
        "Products", "Favorites", "ProductUsages", "UsageEvents",
        "SearchHistories", "AppSettings", "SyncState", "SyncRuns",
    ];

    /// <summary>恢复前强制收拢连接池，确保文件句柄释放。</summary>
    private static void ClearPools()
    {
        SqliteConnection.ClearAllPools();
    }

    /// <summary>构建备份文件名（label 经文件名净化，仅保留安全字符）。</summary>
    private static string BuildBackupFileName(string? label)
    {
        var safeLabel = new string((label ?? string.Empty).Where(char.IsLetterOrDigit).Take(40).ToArray());
        var suffix = safeLabel.Length == 0 ? string.Empty : $"-{safeLabel}";
        return $"gallery-{DateTime.Now:yyyyMMdd-HHmmss}{suffix}.db";
    }

    public async Task<BackupEntry> CreateBackupAsync(string? label = null, CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        var fileName = BuildBackupFileName(label);
        var target = Path.Combine(paths.BackupsDirectory, fileName);

        await using (var connection = OpenConnection(paths.DatabaseFile))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"VACUUM INTO {QuoteString(target)}";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new BackupEntry
        {
            FileName = fileName,
            SizeBytes = new FileInfo(target).Length,
            CreatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
    }

    public Task<IReadOnlyList<BackupEntry>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<BackupEntry>();
        if (Directory.Exists(paths.BackupsDirectory))
        {
            foreach (var file in Directory.GetFiles(paths.BackupsDirectory, "*.db"))
            {
                var info = new FileInfo(file);
                result.Add(new BackupEntry
                {
                    FileName = info.Name,
                    SizeBytes = info.Length,
                    CreatedUnix = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(),
                });
            }
        }

        IReadOnlyList<BackupEntry> sorted = result
            .OrderByDescending(entry => entry.CreatedUnix)
            .ToList();
        return Task.FromResult(sorted);
    }

    public Task DeleteBackupAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var target = Path.Combine(paths.BackupsDirectory, Path.GetFileName(fileName));
        if (File.Exists(target))
        {
            File.Delete(target);
        }

        return Task.CompletedTask;
    }

    public async Task RestoreAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var backupPath = Path.Combine(paths.BackupsDirectory, Path.GetFileName(fileName));
        if (!File.Exists(backupPath))
        {
            throw new BackupValidationException($"备份文件不存在：{fileName}");
        }

        ClearPools();
        await ValidateBackupAsync(backupPath, cancellationToken);

        // 恢复前安全快照：当前库（含可能损坏的状态）先留证。
        var safetyCopy = Path.Combine(
            paths.BackupsDirectory,
            $"pre-restore-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        File.Copy(paths.DatabaseFile, safetyCopy, overwrite: true);

        try
        {
            ReplaceDatabaseFile(backupPath);
        }
        catch (Exception)
        {
            // 替换失败：回滚为恢复前状态。
            File.Copy(safetyCopy, paths.DatabaseFile, overwrite: true);
            throw;
        }
    }

    /// <summary>校验备份文件：quick_check 通过且包含全部必需表。</summary>
    private static async Task ValidateBackupAsync(string backupPath, CancellationToken cancellationToken)
    {
        await using var connection = OpenConnection(backupPath);
        await connection.OpenAsync(cancellationToken);

        var quickCheck = await ExecuteScalarAsync(connection, "PRAGMA quick_check;", cancellationToken) as string;
        if (!string.Equals(quickCheck, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new BackupValidationException($"备份文件损坏（quick_check：{quickCheck}）");
        }

        foreach (var table in RequiredTables)
        {
            var exists = await ExecuteScalarAsync(
                connection,
                $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}';",
                cancellationToken);
            if (Convert.ToInt64(exists) == 0)
            {
                throw new BackupValidationException($"备份文件缺少必需表：{table}");
            }
        }
    }

    /// <summary>原子替换库文件：清除残留 WAL/SHM 后整体复制。</summary>
    private void ReplaceDatabaseFile(string source)
    {
        var target = paths.DatabaseFile;
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = target + suffix;
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
            }
        }

        File.Copy(source, target, overwrite: true);
    }

    private static SqliteConnection OpenConnection(string path)
    {
        return new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString());
    }

    private static async Task<object?> ExecuteScalarAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static string QuoteString(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}
