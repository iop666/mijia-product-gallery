using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Images;
using MijiaProductGallery.Infrastructure.Sync;
using MijiaProductGallery.Tests.Database;
using MijiaProductGallery.Tests.TestSupport;
using MijiaProductGallery.ViewModels;

namespace MijiaProductGallery.Tests;

/// <summary>使用计数服务测试：分类型累计、事件写入、并发原子性。</summary>
public sealed class UsageServiceTests : IAsyncLifetime
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

    private (UsageService Service, Product Product) CreateService()
    {
        var store = new ImageStore(host.Paths);
        var stored = await2(store);
        var context = host.CreateContext();
        var products = new ProductRepository(context);
        var product = new Product
        {
            Model = "zhimi.heater.za1",
            Name = "智米电暖器智能版",
            Brand = "智米",
            Category = "环境电器",
            ImageFileName = stored.ImageFileName,
            ImagePath = stored.ImagePath,
            Sha256 = stored.Sha256,
            FirstSeenUnix = 1,
            LastSeenUnix = 1,
        };
        products.AddAsync(product).Wait();
        var service = new UsageService(new TestDbContextFactory(() => host.CreateContext()));
        return (service, product);
    }

    private static ImageStoreResult await2(ImageStore store)
    {
        return store.StoreNewAsync("zhimi.heater.za1", new MemoryStream(ImageFixtures.CreatePng())).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Record_AccumulatesPerType()
    {
        var (service, product) = CreateService();

        await service.RecordAsync(product.Id, UsageType.View, 1_700_000_100);
        await service.RecordAsync(product.Id, UsageType.Copy, 1_700_000_200);
        await service.RecordAsync(product.Id, UsageType.Copy, 1_700_000_300);
        await service.RecordAsync(product.Id, UsageType.Drag, 1_700_000_400);

        var usage = await new UsageRepository(host.CreateContext()).GetCountsAsync(product.Id);
        Assert.NotNull(usage);
        Assert.Equal(1, usage.ViewCount);
        Assert.Equal(2, usage.CopyCount);
        Assert.Equal(1, usage.DragCount);
        Assert.Equal(4, usage.TotalUseCount);
        Assert.Equal(1_700_000_400, usage.LastUsedUnix);
        Assert.Equal(4, (await new UsageRepository(host.CreateContext()).GetRecentEventsAsync(50)).Count);
    }

    [Fact]
    public async Task Record_Concurrent_IsAtomic()
    {
        var (service, product) = CreateService();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, 100),
            new ParallelOptions { MaxDegreeOfParallelism = 10 },
            async (_, _) => await service.RecordAsync(product.Id, UsageType.Copy, 1_700_000_000));

        var usage = await new UsageRepository(host.CreateContext()).GetCountsAsync(product.Id);
        Assert.NotNull(usage);
        Assert.Equal(100, usage.CopyCount);
        Assert.Equal(100, usage.TotalUseCount);
        Assert.Equal(100, (await new UsageRepository(host.CreateContext()).GetRecentEventsAsync(500)).Count);
    }
}

/// <summary>拖拽手势与策略测试：阈值边界与无图禁拖。</summary>
public sealed class DragGestureTests
{
    [Theory]
    [InlineData(11.9, 0, false)]
    [InlineData(12.0, 0, true)]
    [InlineData(0, -11.9, false)]
    [InlineData(0, -12.0, true)]
    [InlineData(-20, 5, true)]
    public void ShouldStartByMove_UsesThreshold(double dx, double dy, bool expected)
    {
        Assert.Equal(expected, DragGesture.ShouldStartByMove(dx, dy));
    }

    [Theory]
    [InlineData(399, false)]
    [InlineData(400, true)]
    public void ShouldStartByHold_UsesThreshold(int heldMilliseconds, bool expected)
    {
        Assert.Equal(expected, DragGesture.ShouldStartByHold(heldMilliseconds));
    }

    [Fact]
    public void CanDrag_WithoutImage_IsFalse()
    {
        var noImage = new ProductCard(new Product
        {
            Model = "ows.heater.pdeh1a",
            Name = "无图",
            Brand = "b",
            Category = "c",
        });
        Assert.False(DragGesture.CanDrag(noImage));

        var withImage = new ProductCard(new Product
        {
            Model = "a.model.01",
            Name = "有图",
            Brand = "b",
            Category = "c",
            ImageFileName = "a.model.01.png",
            ImagePath = "images/a.model.01.png",
            Sha256 = "aa",
        });
        Assert.True(DragGesture.CanDrag(withImage));
    }
}

/// <summary>剪贴板桩：脚本返回值并记录调用。</summary>
internal sealed class FakeSystemClipboard : ISystemClipboard
{
    public bool ImageResult { get; set; } = true;

    public bool TextResult { get; set; } = true;

    public int ImageCalls { get; private set; }

    public List<string> Texts { get; } = [];

    public Task<bool> SetImageFileAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        ImageCalls++;
        return Task.FromResult(ImageResult);
    }

    public Task<bool> SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        Texts.Add(text);
        return Task.FromResult(TextResult);
    }
}

/// <summary>卡片动作服务测试：复制成功/失败路径、计数联动、收藏事件、文本格式。</summary>
public sealed class CardActionServiceTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private readonly FakeSystemClipboard clipboard = new();

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

    private CardActionService CreateService(out Product product, out ProductCard card)
    {
        var store = new ImageStore(host.Paths);
        var stored = store.StoreNewAsync("zhimi.heater.za1", new MemoryStream(ImageFixtures.CreatePng())).GetAwaiter().GetResult();
        var context = host.CreateContext();
        var products = new ProductRepository(context);
        var usageService = new UsageService(new TestDbContextFactory(() => host.CreateContext()));
        product = new Product
        {
            Model = "zhimi.heater.za1",
            Name = "智米电暖器智能版",
            Brand = "智米",
            Category = "环境电器",
            ImageFileName = stored.ImageFileName,
            ImagePath = stored.ImagePath,
            Sha256 = stored.Sha256,
            FirstSeenUnix = 1,
            LastSeenUnix = 1,
        };
        products.AddAsync(product).Wait();
        card = new ProductCard(product);
        var favoriteService = new FavoriteService(new TestDbContextFactory(() => host.CreateContext()));
        return new CardActionService(store, clipboard, usageService, favoriteService);
    }

    [Fact]
    public async Task CopyImage_Success_IncrementsCopyCount()
    {
        var service = CreateService(out var product, out var card);

        var result = await service.CopyImageAsync(card);

        Assert.True(result.Success);
        Assert.Equal(1, clipboard.ImageCalls);
        var usage = await new UsageRepository(host.CreateContext()).GetCountsAsync(product.Id);
        Assert.Equal(1, usage!.CopyCount);
    }

    [Fact]
    public async Task CopyImage_NoImage_FailsWithoutUsage()
    {
        var service = CreateService(out var product, out _);
        var noImageCard = new ProductCard(new Product
        {
            Model = "ows.heater.pdeh1a",
            Name = "无图",
            Brand = "b",
            Category = "c",
            Id = product.Id + 1,
            FirstSeenUnix = 1,
            LastSeenUnix = 1,
        });

        var result = await service.CopyImageAsync(noImageCard);

        Assert.False(result.Success);
        Assert.Contains("无图片", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(0, clipboard.ImageCalls);
        Assert.Null(await new UsageRepository(host.CreateContext()).GetCountsAsync(noImageCard.ProductId));
    }

    [Fact]
    public async Task CopyImage_ClipboardFails_ReturnsErrorWithoutCount()
    {
        var service = CreateService(out var product, out var card);
        clipboard.ImageResult = false;

        var result = await service.CopyImageAsync(card);

        Assert.False(result.Success);
        Assert.Contains("剪贴板", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Null(await new UsageRepository(host.CreateContext()).GetCountsAsync(product.Id));
    }

    [Fact]
    public async Task CopyImage_MissingFileOnDisk_FailsWithMissingMessage()
    {
        var service = CreateService(out _, out var card);
        File.Delete(Path.Combine(host.Paths.ImagesDirectory, card.ImageFileName!));

        var result = await service.CopyImageAsync(card);

        Assert.False(result.Success);
        Assert.Contains("缺失", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopyText_Kinds_ProduceExpectedText_AndCount()
    {
        var service = CreateService(out var product, out var card);

        await service.CopyTextAsync(card, CardTextKind.Name);
        await service.CopyTextAsync(card, CardTextKind.Model);
        await service.CopyTextAsync(card, CardTextKind.FullInfo);

        Assert.Equal(["智米电暖器智能版", "zhimi.heater.za1", "名称：智米电暖器智能版\n型号：zhimi.heater.za1\n品牌：智米\n分类：环境电器"], clipboard.Texts);
        var usage = await new UsageRepository(host.CreateContext()).GetCountsAsync(product.Id);
        Assert.Equal(3, usage!.CopyCount);
    }

    [Fact]
    public void RequestFavorite_RaisesEvent()
    {
        var service = CreateService(out _, out var card);
        ProductCard? received = null;
        service.FavoriteRequested += c => received = c;

        service.RequestFavorite(card);

        Assert.Same(card, received);
    }
}
