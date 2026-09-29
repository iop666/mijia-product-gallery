using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Tests.TestSupport;

namespace MijiaProductGallery.Tests.Database;

/// <summary>数据库测试宿主：独立临时数据根 + 已初始化的库，测试间互不影响。</summary>
public class DatabaseTestHost : IDisposable
{
    public DatabaseTestHost()
    {
        RootDirectory = Path.Combine(Path.GetTempPath(), "mpg-tests-" + Guid.NewGuid().ToString("N"));
        Paths = new DatabasePaths(RootDirectory);
    }

    public string RootDirectory { get; }

    public DatabasePaths Paths { get; }

    public GalleryDbContext CreateContext()
    {
        return new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Paths.DatabaseFile};Pooling=false")
                .Options);
    }

    public DbInitializer CreateInitializer(GalleryDbContext context)
    {
        return new DbInitializer(context, Paths);
    }

    public static DatabaseTestHost CreateNotInitialized()
    {
        return new DatabaseTestHost();
    }

    public static async Task<DatabaseTestHost> CreateInitializedAsync()
    {
        var host = new DatabaseTestHost();
        await using var context = host.CreateContext();
        await host.CreateInitializer(context).InitializeAsync();
        return host;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>测试用 DbContext 工厂：由委托创建独立上下文（并发安全）。</summary>
public sealed class TestDbContextFactory(Func<GalleryDbContext> factory) : IDbContextFactory<GalleryDbContext>
{
    public GalleryDbContext CreateDbContext()
    {
        return factory();
    }

    public Task<GalleryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(factory());
    }
}
