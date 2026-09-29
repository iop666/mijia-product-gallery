using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using Xunit;

namespace MijiaProductGallery.Tests.Database;

/// <summary>用户数据仓储测试：收藏、合集（级联）、计数与事件、设置、搜索历史。</summary>
public sealed class UserDataRepositoryTests : IDisposable
{
    private readonly DatabaseTestHost _host = DatabaseTestHost.CreateNotInitialized();

    [Fact]
    public async Task Favorites_AddRemoveAndList_SecondAddIsIgnored()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();
        var products = new ProductRepository(context);
        var favorites = new FavoritesRepository(context);

        var product = MakeProduct();
        await products.AddAsync(product);

        await favorites.AddAsync(product.Id, 1_700_000_000);
        await favorites.AddAsync(product.Id, 1_700_000_500);

        Assert.True(await favorites.IsFavoriteAsync(product.Id));
        Assert.Equal([product.Id], await favorites.GetFavoriteProductIdsAsync());
        Assert.Equal(1, await context.Favorites.CountAsync());

        await favorites.RemoveAsync(product.Id);
        Assert.False(await favorites.IsFavoriteAsync(product.Id));
    }

    [Fact]
    public async Task Collections_CreateRenameDelete_CascadeRemovesItems()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();
        var products = new ProductRepository(context);
        var collections = new CollectionRepository(context);

        var product = MakeProduct();
        await products.AddAsync(product);

        var collection = await collections.CreateAsync("PPT素材", 1_700_000_000);
        await collections.AddItemAsync(collection.Id, product.Id, 1_700_000_100);

        await collections.RenameAsync(collection.Id, "PPT 常用", 1_700_000_200);
        var renamed = Assert.Single(await collections.GetAllAsync());
        Assert.Equal("PPT 常用", renamed.Name);

        Assert.Equal([product.Id], await collections.GetProductIdsAsync(collection.Id));

        await collections.DeleteAsync(collection.Id);
        Assert.Empty(await collections.GetAllAsync());
        Assert.Equal(0, await context.CollectionItems.CountAsync());
        Assert.Equal(1, await context.Products.CountAsync());
    }

    [Fact]
    public async Task Collections_DuplicateName_Rejected()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();
        var collections = new CollectionRepository(context);

        await collections.CreateAsync("空调", 1_700_000_000);

        await Assert.ThrowsAsync<DbUpdateException>(() => collections.CreateAsync("空调", 1_700_000_100));
    }

    [Fact]
    public async Task Collections_SameProduct_InMultipleCollections()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();
        var products = new ProductRepository(context);
        var collections = new CollectionRepository(context);

        var product = MakeProduct();
        await products.AddAsync(product);
        var first = await collections.CreateAsync("空调", 1_700_000_000);
        var second = await collections.CreateAsync("常用", 1_700_000_100);
        await collections.AddItemAsync(first.Id, product.Id, 1_700_000_200);
        await collections.AddItemAsync(second.Id, product.Id, 1_700_000_300);
        await collections.AddItemAsync(first.Id, product.Id, 1_700_000_400);

        Assert.Equal(2, await context.CollectionItems.CountAsync());
        Assert.Single(await collections.GetProductIdsAsync(first.Id), product.Id);
    }

    [Fact]
    public async Task Usage_Record_IncrementsCountsPerType_AndWritesEvents()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();
        var products = new ProductRepository(context);
        var usages = new UsageRepository(context);

        var product = MakeProduct();
        await products.AddAsync(product);

        await usages.RecordAsync(product.Id, UsageType.View, 1_700_000_100);
        await usages.RecordAsync(product.Id, UsageType.Copy, 1_700_000_200);
        await usages.RecordAsync(product.Id, UsageType.Drag, 1_700_000_300);

        var counts = await usages.GetCountsAsync(product.Id);
        Assert.NotNull(counts);
        Assert.Equal(1, counts.ViewCount);
        Assert.Equal(1, counts.CopyCount);
        Assert.Equal(1, counts.DragCount);
        Assert.Equal(3, counts.TotalUseCount);
        Assert.Equal(1_700_000_300, counts.LastUsedUnix);

        var events = await usages.GetRecentEventsAsync(10);
        Assert.Equal(3, events.Count);
        Assert.Equal(UsageType.Drag, events[0].Type);
        Assert.Equal(UsageType.View, events[2].Type);

        var limited = await usages.GetRecentEventsAsync(1);
        Assert.Single(limited);
    }

    [Fact]
    public async Task Settings_RoundTrip_AndDefaultOnMissingKey()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();
        var settings = new SettingsRepository(context);

        Assert.Equal("每天", await settings.GetValueAsync("sync.interval", "每天"));

        await settings.SetValueAsync("sync.interval", "每周");
        await settings.SetValueAsync("sync.interval", "关闭");

        Assert.Equal("关闭", await settings.GetValueAsync("sync.interval", "每天"));
        Assert.Equal(1, await context.AppSettings.CountAsync());
    }

    [Fact]
    public async Task SearchHistory_DedupesRefreshesTime_AndClears()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();
        var history = new SearchHistoryRepository(context);

        await history.AddAsync("空气净化器", 1_700_000_100, 5);
        await history.AddAsync("摄像头", 1_700_000_200, 6);
        await history.AddAsync("空气净化器", 1_700_000_300, 5);

        var recent = await history.GetRecentAsync(10);
        Assert.Equal(2, recent.Count);
        Assert.Equal("空气净化器", recent[0].Query);
        Assert.Equal(1_700_000_300, recent[0].CreatedUnix);

        var topOnly = await history.GetRecentAsync(1);
        Assert.Single(topOnly);

        await history.ClearAsync();
        Assert.Empty(await history.GetRecentAsync(10));
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private static Product MakeProduct()
    {
        return new Product
        {
            Model = "zhimi.heater.za1",
            Name = "米家智能电暖器",
            Brand = "小米出品",
            Category = "环境电器",
            FirstSeenUnix = 1_700_000_000,
            LastSeenUnix = 1_700_000_000,
        };
    }
}
