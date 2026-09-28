using MijiaProductGallery.Core;
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

/// <summary>设置系统测试：计数开关生效、缩略图参数运行时更新、设置持久化恢复。</summary>
public sealed class SettingsToggleTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private readonly InMemorySettings settings = new();

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

    private UsageService CreateUsageService()
    {
        return new UsageService(new TestDbContextFactory(() => host.CreateContext()));
    }

    private UsageRepository CreateUsageRepository()
    {
        return new UsageRepository(host.CreateContext());
    }

    private async Task<int> SeedProductAsync(string model)
    {
        await using var context = host.CreateContext();
        var products = new ProductRepository(context);
        var product = new Product { Model = model, Name = model, Brand = "b", Category = "c", FirstSeenUnix = 1, LastSeenUnix = 1 };
        await products.AddAsync(product);
        return product.Id;
    }

    [Fact]
    public async Task UsageService_RespectsRecordViewsToggle()
    {
        var productId = await SeedProductAsync("t.model.01");
        var service = CreateUsageService();
        // 开关写入数据库 AppSettings 表（生产路径：UsageService 经 SettingsRepository 读取）。
        await new SettingsRepository(host.CreateContext()).SetValueAsync(AppSettingsKeys.RecordViews, false);

        await service.RecordAsync(productId, UsageType.View, 1_700_000_000);

        Assert.Null(await CreateUsageRepository().GetCountsAsync(productId));
        Assert.Empty(await CreateUsageRepository().GetRecentEventsAsync(10));
    }

    [Fact]
    public async Task UsageService_RespectsRecordDragsToggle()
    {
        var productId = await SeedProductAsync("t.model.02");
        var service = CreateUsageService();
        await new SettingsRepository(host.CreateContext()).SetValueAsync(AppSettingsKeys.RecordDrags, false);

        await service.RecordAsync(productId, UsageType.Drag, 1_700_000_000);

        var counts = await CreateUsageRepository().GetCountsAsync(productId);
        Assert.True(counts is null || counts.DragCount == 0);
    }

    [Fact]
    public async Task UsageService_DefaultEnabled_RecordsAll()
    {
        var productId = await SeedProductAsync("t.model.03");
        var service = CreateUsageService();

        await service.RecordAsync(productId, UsageType.View, 1_700_000_100);
        await service.RecordAsync(productId, UsageType.Copy, 1_700_000_200);
        await service.RecordAsync(productId, UsageType.Drag, 1_700_000_300);

        var counts = await CreateUsageRepository().GetCountsAsync(productId);
        Assert.NotNull(counts);
        Assert.Equal(3, counts.TotalUseCount);
    }

    [Fact]
    public async Task CopyText_EventRecorded_AsCopyTextType()
    {
        var productId = await SeedProductAsync("t.model.04");
        var service = CreateUsageService();

        await service.RecordAsync(productId, UsageType.CopyText, 1_700_000_000);

        var counts = await CreateUsageRepository().GetCountsAsync(productId);
        Assert.NotNull(counts);
        Assert.Equal(1, counts.CopyCount);
        var evt = Assert.Single(await CreateUsageRepository().GetRecentEventsAsync(10));
        Assert.Equal(UsageType.CopyText, evt.Type);
    }
}

/// <summary>缩略图参数运行时更新测试。</summary>
public sealed class ThumbnailOptionsTests : IAsyncLifetime
{
    private readonly ImageTestHost host = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SmallerMaxEdge_ProducesScaledThumbnail()
    {
        var store = host.CreateStore();
        var stored = await store.StoreNewAsync("t.model.01", new MemoryStream(ImageFixtures.CreatePng(480, 480)));

        var service = host.CreateThumbnailService();
        service.Options.MaxEdge = 240;
        var thumb = await service.EnsureThumbnailAsync(stored.ImageFileName, stored.Sha256);

        using var bitmap = SkiaSharp.SKBitmap.Decode(thumb);
        Assert.Equal(240, bitmap.Info.Width);
    }

    [Fact]
    public void Options_InstanceScoped_DefaultsCorrect()
    {
        var service = host.CreateThumbnailService();
        Assert.Equal(480, service.Options.MaxEdge);
        Assert.Equal(80, service.Options.Quality);
    }
}
