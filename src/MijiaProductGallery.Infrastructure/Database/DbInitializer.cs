using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MijiaProductGallery.Infrastructure.Database;

/// <summary>
/// 数据库初始化器：目录创建 → 完整性校验 → 降级防护 → 迁移前自动备份 → 应用迁移 → 启用 WAL。
/// 任何校验失败都保持库文件原样，不做修复或删除。
/// </summary>
public sealed class DbInitializer(GalleryDbContext context, DatabasePaths paths)
{
    /// <summary>初始化数据库（幂等，可重复调用）。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();

        if (File.Exists(paths.DatabaseFile))
        {
            await EnsureIntegrityAsync(cancellationToken);
            await EnsureNotNewerThanAppAsync(cancellationToken);
            await BackupBeforeMigrationIfNeededAsync(cancellationToken);
        }

        await context.Database.MigrateAsync(cancellationToken);
        await BackfillRandomKeysAsync(cancellationToken);
        await SetWalModeAsync(cancellationToken);
    }

    /// <summary>为随机浏览游标回填随机键（仅处理 null 行，幂等）。</summary>
    private async Task BackfillRandomKeysAsync(CancellationToken cancellationToken)
    {
        var pendingKeys = await context.Products
            .Where(product => product.RandomKey == null)
            .Take(1)
            .ToListAsync(cancellationToken);
        if (pendingKeys.Count == 0)
        {
            return;
        }

        await context.Database.ExecuteSqlRawAsync(
            "UPDATE Products SET RandomKey = ABS(RANDOM()) WHERE RandomKey IS NULL",
            cancellationToken);
    }

    /// <summary>
    /// 备份数据库到 backups\ 目录：先 WAL checkpoint 收敛，再整文件复制。返回备份文件路径。
    /// </summary>
    public async Task<string> BackupDatabaseAsync(string? label = null, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ExecutePragmaAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);

        Directory.CreateDirectory(paths.BackupsDirectory);
        var fileName = $"gallery-{label ?? "backup"}-{DateTime.UtcNow:yyyyMMddHHmmss}.db";
        var target = Path.Combine(paths.BackupsDirectory, fileName);
        File.Copy(paths.DatabaseFile, target, overwrite: true);
        return target;
    }

    private async Task EnsureIntegrityAsync(CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        string? result;
        try
        {
            result = await ExecutePragmaAsync(connection, "PRAGMA quick_check;", cancellationToken);
        }
        catch (SqliteException exception)
        {
            // 文件损坏到无法执行 pragma（如非 SQLite 文件）同样按损坏处理，文件保持原样。
            throw new GalleryDatabaseCorruptException(exception.Message);
        }

        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new GalleryDatabaseCorruptException(result ?? "quick_check 无返回");
        }
    }

    private async Task EnsureNotNewerThanAppAsync(CancellationToken cancellationToken)
    {
        var knownMigrations = context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var historyTableExists = await ExecuteScalarAsync(
            connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory';",
            cancellationToken);
        if (Convert.ToInt64(historyTableExists) == 0)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var unknown = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var migrationId = reader.GetString(0);
            if (!knownMigrations.Contains(migrationId))
            {
                unknown.Add(migrationId);
            }
        }

        if (unknown.Count > 0)
        {
            throw new GalleryDatabaseNewerThanAppException(string.Join(", ", unknown));
        }
    }

    private async Task BackupBeforeMigrationIfNeededAsync(CancellationToken cancellationToken)
    {
        var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pending.Any())
        {
            await BackupDatabaseAsync("pre-migration", cancellationToken);
        }
    }

    private async Task SetWalModeAsync(CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ExecutePragmaAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabaseFile,
        }.ToString());
    }

    private static async Task<string?> ExecutePragmaAsync(
        SqliteConnection connection,
        string pragma,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = pragma;
        return await command.ExecuteScalarAsync(cancellationToken) as string;
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
}
