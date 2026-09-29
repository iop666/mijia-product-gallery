using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Infrastructure.Database;
using Xunit;

namespace MijiaProductGallery.Tests.Database;

/// <summary>数据库初始化测试：建表、幂等、损坏拒动、降级拒动、备份产物。</summary>
public sealed class DbInitializerTests
{
    private static readonly string[] ExpectedTables =
    [
        "Products", "Favorites", "ProductUsages", "Collections", "CollectionItems",
        "UsageEvents", "SearchHistories", "AppSettings", "SyncState", "SyncRuns", "SyncChanges",
    ];

    [Fact]
    public async Task Initialize_CreatesAllTables_AndEnablesWal()
    {
        using var host = DatabaseTestHost.CreateNotInitialized();

        await using (var context = host.CreateContext())
        {
            await host.CreateInitializer(context).InitializeAsync();
        }

        await using var connection = new SqliteConnection($"Data Source={host.Paths.DatabaseFile}");
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (var expected in ExpectedTables)
        {
            Assert.Contains(expected, tables);
        }

        Assert.Equal("wal", await QueryScalarAsync(connection, "PRAGMA journal_mode;"));
    }

    [Fact]
    public async Task Initialize_IsIdempotent()
    {
        using var host = await DatabaseTestHost.CreateInitializedAsync();

        await using var context = host.CreateContext();
        await host.CreateInitializer(context).InitializeAsync();

        Assert.Equal(0, await context.Products.CountAsync());
        Assert.True(File.Exists(host.Paths.DatabaseFile));
    }

    [Fact]
    public async Task CorruptDatabase_InitializationThrows_AndFileStaysUntouched()
    {
        using var host = DatabaseTestHost.CreateNotInitialized();
        host.Paths.EnsureDirectories();
        var corruptedBytes = "这不是一个 SQLite 文件。"u8.ToArray();
        await File.WriteAllBytesAsync(host.Paths.DatabaseFile, corruptedBytes);

        await using var context = host.CreateContext();

        await Assert.ThrowsAsync<GalleryDatabaseCorruptException>(
            () => host.CreateInitializer(context).InitializeAsync());

        SqliteConnection.ClearAllPools();
        Assert.Equal(corruptedBytes, await File.ReadAllBytesAsync(host.Paths.DatabaseFile));
        Assert.Empty(Directory.GetFiles(host.Paths.BackupsDirectory));
    }

    [Fact]
    public async Task DatabaseFromNewerApp_InitializationRefuses_AndFileStaysUntouched()
    {
        using var host = await DatabaseTestHost.CreateInitializedAsync();

        await using (var context = host.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('9999010199999999_FutureMigration', '99.0.0');");
        }

        // 基线取在插入之后：断言的是"初始化器拒绝且不改动文件"，而非库内容无变化。
        SqliteConnection.ClearAllPools();
        var before = await File.ReadAllBytesAsync(host.Paths.DatabaseFile);

        await using var verifyContext = host.CreateContext();
        await Assert.ThrowsAsync<GalleryDatabaseNewerThanAppException>(
            () => host.CreateInitializer(verifyContext).InitializeAsync());

        SqliteConnection.ClearAllPools();
        Assert.Equal(before, await File.ReadAllBytesAsync(host.Paths.DatabaseFile));
    }

    [Fact]
    public async Task BackupDatabase_WritesCopyIntoBackupsDirectory()
    {
        using var host = await DatabaseTestHost.CreateInitializedAsync();

        await using var context = host.CreateContext();
        var backupPath = await host.CreateInitializer(context).BackupDatabaseAsync("test");

        Assert.True(File.Exists(backupPath));
        Assert.StartsWith(host.Paths.BackupsDirectory, backupPath, StringComparison.Ordinal);
    }

    private static async Task<string> QueryScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync()) as string ?? string.Empty;
    }
}
