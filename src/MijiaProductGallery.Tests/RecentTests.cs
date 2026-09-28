using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Images;
using MijiaProductGallery.Infrastructure.Sync;
using MijiaProductGallery.Tests.Database;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>最近使用服务测试：聚合排序、最近事件类型、清空历史边界。</summary>
public sealed class RecentServiceTests : IAsyncLifetime
{
    private const long T0900 = 1_700_000_900;
    private const long T1020 = 1_700_002_000;
    private const long T1015 = 1_700_001_500;

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

    private async Task<Product> AddProductAsync(string model)
    {
        var context = host.CreateContext();
        var products = new ProductRepository(context);
        var product = new Product
        {
            Model = model,
            Name = $"产品 {model}",
            Brand = "b",
            Category = "c",
            FirstSeenUnix = 1,
            LastSeenUnix = 1,
        };
        await products.AddAsync(product);
        return product;
    }

    private async Task RecordAsync(int productId, UsageType type, long at)
    {
        var usageRepository = new UsageRepository(host.CreateContext());
        await usageRepository.RecordAsync(productId, type, at);
    }

    [Fact]
    public async Task GetRecent_EmptyHistory_ReturnsEmpty()
    {
        await AddProductAsync("m-a");
        var service = new RecentService(host.CreateContext());

        Assert.Empty(await service.GetRecentAsync(100));
    }

    [Fact]
    public async Task GetRecent_OrdersByLatestEventTime()
    {
        // 规格：A 10:00 View → 10:20 Copy（最新 10:20）；B 10:15 Drag → A 在 B 前。
        var a = await AddProductAsync("m-a");
        var b = await AddProductAsync("m-b");
        var repository = new UsageRepository(host.CreateContext());
        await repository.RecordAsync(a.Id, UsageType.View, T0900);
        await repository.RecordAsync(a.Id, UsageType.Copy, T1020);
        await repository.RecordAsync(b.Id, UsageType.Drag, T1015);

        var recent = await new RecentService(host.CreateContext()).GetRecentAsync(100);

        Assert.Equal(["m-a", "m-b"], recent.Select(row => row.Product.Model).ToList());
        Assert.Equal(UsageType.Copy, recent[0].LastEvent);
        Assert.Equal(UsageType.Drag, recent[1].LastEvent);
    }

    [Fact]
    public async Task GetRecent_LimitLimitsProductCount()
    {
        var repository = new UsageRepository(host.CreateContext());
        for (var i = 0; i < 5; i++)
        {
            var product = await AddProductAsync($"m-{i:00}");
            await repository.RecordAsync(product.Id, UsageType.View, 1_700_000_000 + i);
        }

        var recent = await new RecentService(host.CreateContext()).GetRecentAsync(3);

        Assert.Equal(3, recent.Count);
    }

    [Fact]
    public async Task GetRecent_SameTime_TiebreakByModel()
    {
        var repository = new UsageRepository(host.CreateContext());
        foreach (var model in new[] { "m-c", "m-a", "m-b" })
        {
            var product = await AddProductAsync(model);
            await repository.RecordAsync(product.Id, UsageType.View, 1_700_000_000);
        }

        var recent = await new RecentService(host.CreateContext()).GetRecentAsync(100);
        Assert.Equal(["m-a", "m-b", "m-c"], recent.Select(row => row.Product.Model).ToList());
    }

    [Fact]
    public async Task ClearHistory_RemovesEventsAndCounts_KeepsFavoritesAndSearch()
    {
        var product = await AddProductAsync("m-a");
        var usageRepository = new UsageRepository(host.CreateContext());
        await usageRepository.RecordAsync(product.Id, UsageType.Copy, 1_700_000_000);
        var favorites = new FavoritesRepository(host.CreateContext());
        await favorites.AddAsync(product.Id, 1_700_000_000);
        var searchHistory = new SearchHistoryRepository(host.CreateContext());
        await searchHistory.AddAsync("空气", 1_700_000_000, 5);

        await new RecentService(host.CreateContext()).ClearHistoryAsync();

        await using var context = host.CreateContext();
        Assert.Equal(0, await context.UsageEvents.CountAsync());
        Assert.Equal(0, await context.ProductUsages.CountAsync());
        Assert.Equal(1, await context.Favorites.CountAsync());
        Assert.Equal(1, await context.SearchHistories.CountAsync());
    }
}

/// <summary>RecordAsync 对 CopyText 的支持（事件类型独立、计入 CopyCount）。</summary>
public sealed class CopyTextRecordingTests : IAsyncLifetime
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

    [Fact]
    public async Task Record_CopyText_IncrementsCopyCount_WithCopyTextEventType()
    {
        await using var context = host.CreateContext();
        var products = new ProductRepository(context);
        var product = new Product { Model = "a.model.01", Name = "甲", Brand = "b", Category = "c", FirstSeenUnix = 1, LastSeenUnix = 1 };
        await products.AddAsync(product);
        var usageRepository = new UsageRepository(context);

        await usageRepository.RecordAsync(product.Id, UsageType.CopyText, 1_700_000_000);

        var counts = await usageRepository.GetCountsAsync(product.Id);
        Assert.NotNull(counts);
        Assert.Equal(1, counts.CopyCount);
        Assert.Equal(1, counts.TotalUseCount);
        var evt = Assert.Single(await usageRepository.GetRecentEventsAsync(10));
        Assert.Equal(UsageType.CopyText, evt.Type);
    }
}

/// <summary>同步防污染测试：同步不产生、不删除行为记录。</summary>
public sealed class SyncUsagePollutionTests : IDisposable
{
    private readonly SyncTestHost host = new();

    [Fact]
    public async Task Sync_PreservesUsageEventsAndCounts()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Database.Paths).InitializeAsync();
        var products = new ProductRepository(context);
        var product = new Product { Model = "a.model.01", Name = "甲", Brand = "b", Category = "c", FirstSeenUnix = 1, LastSeenUnix = 1 };
        await products.AddAsync(product);
        var usageRepository = new UsageRepository(context);
        for (var i = 0; i < 100; i++)
        {
            await usageRepository.RecordAsync(product.Id, UsageType.Copy, 1_700_000_000 + i);
        }

        host.Api.AddCategory(9, "其他");
        host.Api.AddProduct(9, "a.model.01", "甲", "b", 1, 2);

        await (await host.CreateEngineAsync()).SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(100, await context.UsageEvents.CountAsync());
        var counts = await usageRepository.GetCountsAsync(product.Id);
        Assert.NotNull(counts);
        Assert.Equal(100, counts.TotalUseCount);
    }

    public void Dispose()
    {
        host.Dispose();
    }
}
