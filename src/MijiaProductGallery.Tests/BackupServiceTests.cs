using Microsoft.Data.Sqlite;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Tests.Database;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>备份/恢复服务测试：快照内容、校验拒绝、恢复回滚与列表管理。</summary>
public sealed class BackupServiceTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();
    }

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    private BackupService CreateService()
    {
        return new BackupService(host.Paths);
    }

    private async Task SeedDataAsync(string name)
    {
        await using var context = host.CreateContext();
        var products = new ProductRepository(context);
        var favorites = new FavoritesRepository(context);
        var product = new MijiaProductGallery.Core.Models.Product
        {
            Model = name,
            Name = $"产品 {name}",
            Brand = "b",
            Category = "c",
            FirstSeenUnix = 1,
            LastSeenUnix = 1,
        };
        await products.AddAsync(product);
        await favorites.AddAsync(product.Id, 1_700_000_000);
    }

    [Fact]
    public async Task CreateBackup_ProducesValidSnapshot_WithAllData()
    {
        await SeedDataAsync("m-a");
        var service = CreateService();

        var entry = await service.CreateBackupAsync();

        Assert.True(entry.SizeBytes > 0);
        Assert.StartsWith("gallery-", entry.FileName);
        Assert.True(File.Exists(Path.Combine(host.Paths.BackupsDirectory, entry.FileName)));

        // 备份文件是合法 SQLite 且数据完整。
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(host.Paths.BackupsDirectory, entry.FileName)};Pooling=false");
        await connection.OpenAsync();
        var productCount = Convert.ToInt64(ExecuteScalar(connection, "SELECT COUNT(*) FROM Products;"));
        var favoriteCount = Convert.ToInt64(ExecuteScalar(connection, "SELECT COUNT(*) FROM Favorites;"));
        Assert.Equal(1, productCount);
        Assert.Equal(1, favoriteCount);
    }

    [Fact]
    public async Task ListBackups_OrdersNewestFirst()
    {
        var service = CreateService();
        var first = await service.CreateBackupAsync("first");
        await Task.Delay(1100); // 文件名秒级精度：确保第二个文件名不同
        var second = await service.CreateBackupAsync("second");

        var list = await service.ListBackupsAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal(second.FileName, list[0].FileName);
        Assert.Equal(first.FileName, list[1].FileName);
    }

    [Fact]
    public async Task Restore_RollsBackToSnapshot_State()
    {
        var service = CreateService();
        var contextFactory = new TestDbContextFactory(() => host.CreateContext());

        // 第一阶段：a.model.01 + 收藏 → 备份。
        await using (var context = host.CreateContext())
        {
            var products = new ProductRepository(context);
            var favorites = new FavoritesRepository(context);
            var product = new MijiaProductGallery.Core.Models.Product
            {
                Model = "a.model.01",
                Name = "甲",
                Brand = "b",
                Category = "c",
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            };
            await products.AddAsync(product);
            await favorites.AddAsync(product.Id, 1_700_000_000);
        }

        var backup = await service.CreateBackupAsync();

        // 第二阶段：破坏当前状态（删产品行→收藏级联消失，再加新行）。
        await using (var context = host.CreateContext())
        {
            await context.Products.Where(p => p.Model == "a.model.01").ExecuteDeleteAsync();
            await context.Products.AddAsync(new MijiaProductGallery.Core.Models.Product
            {
                Model = "zz.model.99",
                Name = "新增",
                Brand = "b",
                Category = "c",
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            });
            await context.SaveChangesAsync();
        }

        await service.RestoreAsync(backup.FileName);

        await using var verify = host.CreateContext();
        Assert.Equal(1, await verify.Products.CountAsync());
        Assert.Equal("a.model.01", (await verify.Products.AsNoTracking().SingleAsync()).Model);
        Assert.Equal(1, await verify.Favorites.CountAsync());
    }

    [Fact]
    public async Task Restore_CorruptBackup_ThrowsAndCurrentDbUntouched()
    {
        var service = CreateService();
        var corrupted = Path.Combine(host.Paths.BackupsDirectory, "gallery-corrupt.db");
        await File.WriteAllTextAsync(corrupted, "这不是 SQLite 文件");

        await Assert.ThrowsAnyAsync<Exception>(() => service.RestoreAsync(corrupted));

        // 当前库保持可用（quick_check ok）。
        await using var context = host.CreateContext();
        Assert.Equal(0, await context.Products.CountAsync());
    }

    [Fact]
    public async Task Restore_BackupMissingTable_Throws()
    {
        var service = CreateService();
        var partial = Path.Combine(host.Paths.BackupsDirectory, "gallery-partial.db");
        await using (var connection = new SqliteConnection($"Data Source={partial};Pooling=false"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE OnlyOne (Id INTEGER PRIMARY KEY); INSERT INTO OnlyOne VALUES (1);";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAnyAsync<Exception>(() => service.RestoreAsync(partial));

        await using var context = host.CreateContext();
        Assert.Equal(0, await context.Products.CountAsync());
    }

    [Fact]
    public async Task DeleteBackup_RemovesFile()
    {
        var service = CreateService();
        var backup = await service.CreateBackupAsync();

        await service.DeleteBackupAsync(backup.FileName);

        Assert.False(File.Exists(Path.Combine(host.Paths.BackupsDirectory, backup.FileName)));
    }

    private static object? ExecuteScalar(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}

/// <summary>备份文件名净化测试。</summary>
public sealed class BackupFileNameTests
{
    [Fact]
    public void BuildBackupFileName_SanitizesLabel()
    {
        // 通过反射调用私有静态方法（验证非法字符过滤）。
        var method = typeof(MijiaProductGallery.Infrastructure.Database.BackupService)
            .GetMethod("BuildBackupFileName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        var fileName = (string)method!.Invoke(null, new object?[] { "pre restore/../x" })!;
        Assert.DoesNotContain("/", fileName);
        Assert.DoesNotContain("..", fileName);
        Assert.StartsWith("gallery-", fileName);
    }
}
