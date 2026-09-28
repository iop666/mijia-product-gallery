using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Images;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>同步引擎测试：新增/下架/换图/ID 复用/下载失败保旧态/幂等/状态重置/货架归类。</summary>
public sealed class SyncEngineTests : IDisposable
{
    private readonly SyncTestHost host = new();

    [Fact]
    public async Task Sync_CreatesNewProducts_WithImagesAndThumbnails()
    {
        var bytes = ImageFixtures.CreatePng();
        host.Api.AddCategory(8, "运动健康");
        host.Api.AddProduct(8, "miwu.band.pro", "米家手环 Pro", "小米出品", 1_700_000_000, 1_700_000_100);
        host.Api.AddProduct(8, "miwu.scale.s400", "米家体脂秤 S400", "小米出品", 1_700_000_000, 1_700_000_100);
        foreach (var model in new[] { "miwu.band.pro", "miwu.scale.s400" })
        {
            host.Downloader.Responses[IconUrl(model)] = bytes;
        }

        var run = await (await host.CreateEngineAsync()).SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, run.Status);
        Assert.Equal(SyncStage.Completed, run.Stage);
        Assert.Equal(2, run.Counts!.NewCount);

        await using var context = host.CreateContext();
        var products = new ProductRepository(context);
        Assert.Equal(2, await products.CountAsync());
        var band = await products.GetByModelAsync("miwu.band.pro");
        Assert.NotNull(band);
        Assert.Equal("运动健康", band.Category);
        Assert.Equal("images/miwu.band.pro.png", band.ImagePath);
        Assert.NotNull(band.Sha256);
        Assert.True(File.Exists(Path.Combine(host.Database.Paths.ImagesDirectory, "miwu.band.pro.png")));
        Assert.NotEmpty(Directory.GetFiles(host.Database.Paths.ThumbnailsDirectory, "miwu.band.pro.*.webp"));

        var state = await new SyncStateRepository(context).GetStateAsync();
        Assert.Equal(SyncStatus.Success, state.Status);
        Assert.NotNull(state.LastSuccessfulSyncUnix);
        Assert.NotNull(state.SnapshotDate);
    }

    [Fact]
    public async Task Sync_MarksDelisted_KeepsImageOnDisk()
    {
        var bytes = ImageFixtures.CreatePng();
        var seeded = await host.SeedProductWithImageAsync("midjd.fridge.bs42s", "米家冰箱", "厨房电器", bytes);
        host.Api.AddCategory(3, "厨房电器");

        var run = await (await host.CreateEngineAsync()).SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, run.Status);
        Assert.Equal(1, run.Counts!.DelistedCount);

        await using var context = host.CreateContext();
        var row = await new ProductRepository(context).GetByModelAsync("midjd.fridge.bs42s");
        Assert.NotNull(row);
        Assert.False(row.IsAvailable);
        Assert.Equal(seeded.Sha256, row.Sha256);
        Assert.True(File.Exists(Path.Combine(host.Database.Paths.ImagesDirectory, "midjd.fridge.bs42s.png")));

        var change = Assert.Single(await context.SyncChanges.ToListAsync());
        Assert.Equal(ChangeType.Delisted, change.Type);
    }

    [Fact]
    public async Task Sync_ImageChanged_ReplacesFileWithoutHistory()
    {
        var v1 = ImageFixtures.CreatePng(64, 32);
        var v2 = ImageFixtures.CreatePng(32, 64);
        await host.SeedProductWithImageAsync("zhimi.heater.za1", "米家智能电暖器", "环境电器", v1);
        host.Api.AddCategory(7, "环境电器");
        host.Api.AddProduct(7, "zhimi.heater.za1", "米家智能电暖器", "小米出品", 1_500_000_000, 1_600_000_100);
        host.Downloader.Responses[IconUrl("zhimi.heater.za1")] = v2;

        var run = await (await host.CreateEngineAsync()).SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(1, run.Counts!.ImageChangedCount);
        await using var context = host.CreateContext();
        var row = await new ProductRepository(context).GetByModelAsync("zhimi.heater.za1");
        Assert.NotNull(row);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(v2)).ToLowerInvariant(), row.Sha256);
        Assert.Equal(v2, await File.ReadAllBytesAsync(Path.Combine(host.Database.Paths.ImagesDirectory, "zhimi.heater.za1.png")));
        Assert.Empty(Directory.GetFiles(host.Database.Paths.ImagesDirectory, "zhimi.heater.za1.old*"));
    }

    [Fact]
    public async Task Sync_IdReused_MovesOldImageToChain_AndRecordsChange()
    {
        var camera = ImageFixtures.CreatePng(64, 32);
        var lockDevice = ImageFixtures.CreatePng(32, 64);
        await host.SeedProductWithImageAsync("chuangmi.camera.029a02", "小米智能摄像机", "安防", camera);
        host.Api.AddCategory(5, "安防");
        host.Api.AddProduct(5, "chuangmi.camera.029a02", "小米智能门锁", "小米出品", 1_500_000_000, 1_600_000_100);
        host.Downloader.Responses[IconUrl("chuangmi.camera.029a02")] = lockDevice;

        var run = await (await host.CreateEngineAsync()).SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(1, run.Counts!.IdReusedCount);
        var imagesDirectory = host.Database.Paths.ImagesDirectory;
        Assert.Equal(camera, await File.ReadAllBytesAsync(Path.Combine(imagesDirectory, "chuangmi.camera.029a02.old.png")));
        Assert.Equal(lockDevice, await File.ReadAllBytesAsync(Path.Combine(imagesDirectory, "chuangmi.camera.029a02.png")));

        await using var context = host.CreateContext();
        var row = await new ProductRepository(context).GetByModelAsync("chuangmi.camera.029a02");
        Assert.Equal("小米智能门锁", row!.Name);
        var change = Assert.Single(await context.SyncChanges.ToListAsync());
        Assert.Equal(ChangeType.IdReused, change.Type);
        Assert.Equal("chuangmi.camera.029a02.old.png", change.Change.ReplacedByOldFileName);
        Assert.Equal("小米智能摄像机", change.Change.OldName);
    }

    [Fact]
    public async Task Sync_ImageDownloadFails_KeepsOldState()
    {
        var v1 = ImageFixtures.CreatePng(64, 32);
        var seeded = await host.SeedProductWithImageAsync("zhimi.heater.za1", "米家智能电暖器", "环境电器", v1);
        host.Api.AddCategory(7, "环境电器");
        host.Api.AddProduct(7, "zhimi.heater.za1", "米家智能电暖器", "小米出品", 1_500_000_000, 1_600_000_100);
        host.Downloader.FailingUrls.Add(IconUrl("zhimi.heater.za1"));

        var run = await (await host.CreateEngineAsync()).SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, run.Status);
        Assert.Equal(1, run.Counts!.ImageFailureCount);

        await using var context = host.CreateContext();
        var row = await new ProductRepository(context).GetByModelAsync("zhimi.heater.za1");
        Assert.NotNull(row);
        Assert.Equal(seeded.Sha256, row.Sha256);
        Assert.Equal(v1, await File.ReadAllBytesAsync(Path.Combine(host.Database.Paths.ImagesDirectory, "zhimi.heater.za1.png")));
        Assert.Equal(0, await context.SyncChanges.CountAsync());
    }

    [Fact]
    public async Task Sync_NewProductImageFailure_AddsProductWithoutImage()
    {
        host.Api.AddCategory(13, "传感器");
        host.Api.AddProduct(13, "anseny.sensor.new", "Anseny 新温湿度计", "Anseny", 1_700_000_000, 1_700_000_100);
        host.Downloader.FailingUrls.Add(IconUrl("anseny.sensor.new"));

        var run = await (await host.CreateEngineAsync()).SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, run.Status);
        Assert.Equal(1, run.Counts!.ImageFailureCount);
        Assert.Equal(1, run.Counts.NewCount);

        await using var context = host.CreateContext();
        var row = await new ProductRepository(context).GetByModelAsync("anseny.sensor.new");
        Assert.NotNull(row);
        Assert.Null(row.ImageFileName);
        Assert.Null(row.Sha256);
        Assert.Equal(IconUrl("anseny.sensor.new"), row.ImageUrl);
    }

    [Fact]
    public async Task Sync_DoesNotTouchFavorites()
    {
        var bytes = ImageFixtures.CreatePng();
        var seededProduct = await host.SeedProductWithImageAsync("a.model.01", "甲", "其他", bytes);
        var favorites = new FavoritesRepository(host.CreateContext());
        await favorites.AddAsync(seededProduct.Id, 1_700_000_000);

        host.Api.AddCategory(9, "其他");
        host.Api.AddProduct(9, "a.model.01", "甲（改名）", "b", 1, 2);
        host.Downloader.Responses[IconUrl("a.model.01")] = bytes;

        var run = await (await host.CreateEngineAsync()).SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, run.Status);
        Assert.True(await favorites.IsFavoriteAsync(seededProduct.Id));
        Assert.Equal(1, await host.CreateContext().Favorites.CountAsync());
    }

    [Fact]
    public async Task Sync_DelistedProduct_FavoriteRowRetained()
    {
        var bytes = ImageFixtures.CreatePng();
        var seededProduct = await host.SeedProductWithImageAsync("midjd.fridge.bs42s", "已下架冰箱", "厨房电器", bytes);
        var favorites = new FavoritesRepository(host.CreateContext());
        await favorites.AddAsync(seededProduct.Id, 1_700_000_000);

        host.Api.AddCategory(3, "厨房电器");

        await (await host.CreateEngineAsync()).SyncNowAsync(SyncTrigger.Manual);

        Assert.True(await favorites.IsFavoriteAsync(seededProduct.Id));
        var row = await host.CreateContext().Favorites.AsNoTracking().SingleAsync();
        Assert.Equal(seededProduct.Id, row.ProductId);
    }

    [Fact]
    public async Task Sync_SecondRun_MakesNoChanges_AndSkipsDownloads()
    {
        var bytes = ImageFixtures.CreatePng();
        host.Api.AddCategory(8, "运动健康");
        host.Api.AddProduct(8, "miwu.band.pro", "米家手环 Pro", "小米出品", 1_700_000_000, 1_700_000_100);
        host.Downloader.Responses[IconUrl("miwu.band.pro")] = bytes;
        var engine = await host.CreateEngineAsync();

        var first = await engine.SyncNowAsync(SyncTrigger.Manual);
        var callsAfterFirst = host.Downloader.CallCount;
        var second = await engine.SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, second.Status);
        Assert.Equal(0, second.Counts!.NewCount);
        Assert.Equal(0, second.Counts.ImageChangedCount);
        Assert.Equal(0, second.Counts.IdReusedCount);
        Assert.Equal(0, second.Counts.DelistedCount);
        Assert.Equal(callsAfterFirst, host.Downloader.CallCount);
    }

    [Fact]
    public async Task Sync_ResetsLeftoverRunningState()
    {
        await host.EnsureInitializedAsync();
        await using (var context = host.CreateContext())
        {
            var syncState = new SyncStateRepository(context);
            await syncState.SaveStateAsync(
                new SyncState { Id = 1, Status = SyncStatus.Running, Stage = SyncStage.DownloadingImages },
                CancellationToken.None);
        }

        host.Api.AddCategory(9, "其他");
        var run = await (await host.CreateEngineAsync()).SyncNowAsync(SyncTrigger.Startup);

        Assert.Equal(SyncStatus.Success, run.Status);
        await using var verify = host.CreateContext();
        var state = await new SyncStateRepository(verify).GetStateAsync();
        Assert.Equal(SyncStatus.Success, state.Status);
    }

    [Fact]
    public async Task Sync_ShelfOnlyProduct_GoesToNewArrivals()
    {
        host.Api.AddCategory(-10000, "新上线");
        host.Api.AddProduct(-10000, "xiaomi.airp.mp5b", "米家空气净化器 5 小米出品", "小米出品", 1_700_000_000, 1_700_000_100);

        var run = await (await host.CreateEngineAsync()).SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(1, run.Counts!.NewCount);
        await using var context = host.CreateContext();
        var row = await new ProductRepository(context).GetByModelAsync("xiaomi.airp.mp5b");
        Assert.Equal("新上线", row!.Category);
    }

    public void Dispose()
    {
        host.Dispose();
    }

    private static string IconUrl(string model)
    {
        return $"https://cdn.cnbj1.fds.api.mi-img.com/iotweb-product-center/{model}.png";
    }
}
