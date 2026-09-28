using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;
using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Images;
using MijiaProductGallery.Infrastructure.Sync;
using MijiaProductGallery.Tests.Database;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>收藏服务测试：幂等语义（重复添加/重复移除/切换）。</summary>
public sealed class FavoriteServiceTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();
        var products = new ProductRepository(context);
        await products.AddAsync(new Product
        {
            Model = "zhimi.heater.za1",
            Name = "智米电暖器智能版",
            Brand = "智米",
            Category = "环境电器",
            FirstSeenUnix = 1,
            LastSeenUnix = 1,
        });
    }

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    private FavoriteService CreateService()
    {
        return new FavoriteService(new TestDbContextFactory(() => host.CreateContext()));
    }

    [Fact]
    public async Task Add_Twice_IsIdempotent()
    {
        var productId = (await host.CreateContext().Products.AsNoTracking().SingleAsync(p => p.Model == "zhimi.heater.za1")).Id;
        var service = CreateService();

        await service.AddAsync(productId);
        await service.AddAsync(productId);

        Assert.True(await service.IsFavoriteAsync(productId));
        await using var context = host.CreateContext();
        Assert.Equal(1, await context.Favorites.CountAsync());
    }

    [Fact]
    public async Task Remove_Twice_IsIdempotent()
    {
        var productId = (await host.CreateContext().Products.AsNoTracking().SingleAsync(p => p.Model == "zhimi.heater.za1")).Id;
        var service = CreateService();
        await service.AddAsync(productId);

        await service.RemoveAsync(productId);
        await service.RemoveAsync(productId);

        Assert.False(await service.IsFavoriteAsync(productId));
    }

    [Fact]
    public async Task Toggle_AlternatesState()
    {
        var productId = (await host.CreateContext().Products.AsNoTracking().SingleAsync(p => p.Model == "zhimi.heater.za1")).Id;
        var service = CreateService();

        Assert.True(await service.ToggleAsync(productId));
        Assert.True(await service.IsFavoriteAsync(productId));

        Assert.False(await service.ToggleAsync(productId));
        Assert.False(await service.IsFavoriteAsync(productId));
    }

    [Fact]
    public async Task Toggle_LaterImportedProduct_Works()
    {
        // 后导入的产品获得新 Id：收藏服务按 Id 幂等切换，行为一致。
        await using var context = host.CreateContext();
        var products = new ProductRepository(context);
        await products.AddAsync(new Product
        {
            Model = "late.model.01",
            Name = "后导入产品",
            Brand = "b",
            Category = "c",
            FirstSeenUnix = 1,
            LastSeenUnix = 1,
        });
        var late = (await new ProductRepository(host.CreateContext()).GetByModelAsync("late.model.01"))!;

        var service = CreateService();
        var firstState = await service.ToggleAsync(late.Id);
        var secondState = await service.ToggleAsync(late.Id);
        Assert.True(firstState);
        Assert.False(secondState);
        Assert.False(await service.IsFavoriteAsync(late.Id));
    }
}

/// <summary>收藏筛选查询测试：收藏维度进管线并与搜索/分类/排序组合（真实 SQLite）。</summary>
public sealed class FavoriteFilterTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();
        var products = new ProductRepository(context);
        await products.AddRangeAsync(
        [
            new Product { Model = "a.model.01", Name = "甲", Brand = "小米出品", Category = "环境电器", FirstSeenUnix = 1, LastSeenUnix = 1 },
            new Product { Model = "b.model.02", Name = "乙", Brand = "小米出品", Category = "个护与起居", FirstSeenUnix = 1, LastSeenUnix = 1 },
            new Product { Model = "c.model.03", Name = "丙", Brand = "其他", Category = "环境电器", FirstSeenUnix = 1, LastSeenUnix = 1 },
        ]);
        var favorites = new FavoritesRepository(context);
        await favorites.AddAsync((await products.GetByModelAsync("a.model.01"))!.Id, 1_700_000_000);
    }

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    private async Task<List<string>> QueryModelsAsync(ProductQuery query)
    {
        var factory = new TestDbContextFactory(() => host.CreateContext());
        var service = new ProductQueryService(factory, new FilterService(), new SortService());
        var rows = await service.QueryAsync(query);
        return rows.Select(product => product.Model).ToList();
    }

    [Fact]
    public async Task FavoriteTrue_ReturnsOnlyFavorites()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Filter = new ProductFilter { IsFavorite = true },
        });
        Assert.Equal(["a.model.01"], models);
    }

    [Fact]
    public async Task FavoriteFalse_ReturnsOnlyNonFavorites()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Filter = new ProductFilter { IsFavorite = false },
        });
        Assert.Equal(["b.model.02", "c.model.03"], models);
    }

    [Fact]
    public async Task Favorite_WithKeyword_IsIntersection()
    {
        var models = await QueryModelsAsync(new ProductQuery
        {
            Keyword = "丙",
            Filter = new ProductFilter { IsFavorite = true },
        });
        Assert.Empty(models);

        models = await QueryModelsAsync(new ProductQuery
        {
            Keyword = "甲",
            Filter = new ProductFilter { IsFavorite = true },
        });
        Assert.Equal(["a.model.01"], models);
    }

    [Fact]
    public async Task Favorite_DelistedProduct_RetainedInFavorites()
    {
        // 下架不移除收藏：收藏的是产品记录，不是在售状态。
        var models = await QueryModelsAsync(new ProductQuery
        {
            Filter = new ProductFilter { IsFavorite = true },
        });
        Assert.Contains("a.model.01", models);
    }

    [Fact]
    public async Task Pipeline_Keyword_Category_Favorite_Sort_Combined()
    {
        var factory = new TestDbContextFactory(() => host.CreateContext());
        var service = new ProductQueryService(factory, new FilterService(), new SortService());
        var rows = await service.QueryAsync(new ProductQuery
        {
            Keyword = "甲",
            Filter = new ProductFilter
            {
                Categories = ["环境电器"],
                IsFavorite = true,
            },
            Sort = new ProductSort { Field = ProductSortField.Model, Direction = SortDirection.Ascending },
        });

        var model = Assert.Single(rows);
        Assert.Equal("a.model.01", model.Model);
    }
}
