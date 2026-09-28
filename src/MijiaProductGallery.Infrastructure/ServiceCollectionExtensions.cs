using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;

namespace MijiaProductGallery.Infrastructure;

/// <summary>Infrastructure 层依赖注入注册。</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>注册数据目录、DbContext、初始化器与全部仓储。可传替代数据根（测试/便携模式）。</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? dataRootOverride = null)
    {
        var paths = new DatabasePaths(dataRootOverride);
        services.AddSingleton(paths);
        services.AddDbContext<GalleryDbContext>(options => options.UseSqlite($"Data Source={paths.DatabaseFile}"));
        services.AddScoped<DbInitializer>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IFavoritesRepository, FavoritesRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<IUsageRepository, UsageRepository>();
        services.AddScoped<ISearchHistoryRepository, SearchHistoryRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<ISyncStateRepository, SyncStateRepository>();
        return services;
    }
}
