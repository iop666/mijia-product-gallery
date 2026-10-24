using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Http;
using MijiaProductGallery.Infrastructure.Images;
using MijiaProductGallery.Infrastructure.Seed;
using MijiaProductGallery.Infrastructure.Sync;

namespace MijiaProductGallery.Infrastructure;

/// <summary>Infrastructure 层依赖注入注册。</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>注册数据目录、DbContext、初始化器、图片系统、同步引擎、种子导入与全部仓储。可传替代数据根（测试/便携模式）。</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? dataRootOverride = null)
    {
        var paths = new DatabasePaths(dataRootOverride);
        // 容器构建即创建全部数据目录：先于任何数据库访问（SQLite 打开不存在的目录会抛）。
        paths.EnsureDirectories();
        services.AddSingleton(paths);
        services.AddSingleton<Core.Interfaces.IAppDataRoot>(paths);
        services.AddDbContext<GalleryDbContext>(options => options.UseSqlite($"Data Source={paths.DatabaseFile}"));
        services.AddDbContextFactory<GalleryDbContext>(options => options.UseSqlite($"Data Source={paths.DatabaseFile}"));
        services.AddScoped<DbInitializer>();
        services.AddSingleton<IImageStore, ImageStore>();
        services.AddSingleton<IFilterService, FilterService>();
        services.AddSingleton<ISortService, SortService>();
        services.AddSingleton<IRecentService, RecentService>();
        services.AddSingleton<IProductQueryService, ProductQueryService>();
        services.AddSingleton<IUsageStatisticsService, UsageStatisticsService>();
        services.AddSingleton<IAutoSyncScheduler, AutoSyncScheduler>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<ThumbnailSettings>();
        services.AddSingleton<ILibraryRuntimeOptions, LibraryRuntimeOptions>();
        services.AddSingleton<IThumbnailService, ThumbnailService>();
        services.AddSingleton<IBaikeApiClient, BaikeApiClient>();
        services.AddSingleton<IImageDownloader, ImageDownloader>();
        services.AddScoped<ISeedImporter, SeedImporter>();
        services.AddScoped<IFirstRunInitializer, FirstRunInitializer>();

        // 仓储/服务为无状态对象：每次数据库操作经 IDbContextFactory 新建上下文后立即释放。
        // 绝不能把同一个 GalleryDbContext 注入多个单例（EF Core 非线程安全，并发即抛
        // "A second operation was started on this context"）。
        services.AddSingleton<IProductRepository>(
            provider => new ProductRepository(provider.GetRequiredService<IDbContextFactory<GalleryDbContext>>()));
        services.AddSingleton<IFavoritesRepository>(
            provider => new FavoritesRepository(provider.GetRequiredService<IDbContextFactory<GalleryDbContext>>()));
        services.AddSingleton<ICollectionRepository>(
            provider => new CollectionRepository(provider.GetRequiredService<IDbContextFactory<GalleryDbContext>>()));
        services.AddSingleton<IUsageRepository>(
            provider => new UsageRepository(provider.GetRequiredService<IDbContextFactory<GalleryDbContext>>()));
        services.AddSingleton<ISearchHistoryRepository>(
            provider => new SearchHistoryRepository(provider.GetRequiredService<IDbContextFactory<GalleryDbContext>>()));
        services.AddSingleton<ISettingsRepository>(
            provider => new SettingsRepository(provider.GetRequiredService<IDbContextFactory<GalleryDbContext>>()));
        services.AddSingleton<ISyncStateRepository>(
            provider => new SyncStateRepository(provider.GetRequiredService<IDbContextFactory<GalleryDbContext>>()));
        services.AddSingleton<IRecentService>(
            provider => new RecentService(provider.GetRequiredService<IDbContextFactory<GalleryDbContext>>()));
        services.AddSingleton<ISearchService>(
            provider => new SearchService(provider.GetRequiredService<IDbContextFactory<GalleryDbContext>>()));
        services.AddSingleton<ISyncService, SyncEngine>();
        return services;
    }
}
