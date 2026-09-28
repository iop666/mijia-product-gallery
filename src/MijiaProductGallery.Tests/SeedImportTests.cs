using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Images;
using MijiaProductGallery.Infrastructure.Seed;
using MijiaProductGallery.Infrastructure.Sync;
using MijiaProductGallery.Tests.Database;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>Seed Package 导入测试：校验矩阵、幂等、恢复、用户数据隔离。</summary>
public sealed class SeedImportTests : IDisposable, IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private SeedImporter CreateImporter()
    {
        return new SeedImporter(host.CreateContext(), host.Paths);
    }

    [Fact]
    public async Task Import_ValidPackage_CreatesCompleteGallery()
    {
        var png = ImageFixtures.CreatePng();
        var jpg = ImageFixtures.CreateJpeg();
        var old = ImageFixtures.CreatePng(16, 16);
        var package = await SeedPackFixture.WriteAsync(TempDirectory(), "2026-09-28",
        [
            new SeedLineFixture { Model = "zhimi.heater.za1", Name = "米家智能电暖器", Category = "环境电器" }
                .WithImage("zhimi.heater.za1.png", png)
                .WithOldImage("zhimi.heater.za1.old.png", old),
            new SeedLineFixture
                {
                    Model = "tuyun.camera.abc",
                    Name = "「涂鸦」摄像机, 高清版",
                    Brand = "涂鸦",
                    Category = "安防",
                }
                .WithImage("tuyun.camera.abc.jpg", jpg),
            new SeedLineFixture
                {
                    Model = "aux.aircondition.hc1",
                    Name = "aux 空调伴侣",
                    Category = "环境电器",
                    ImageMissing = true,
                }
                .WithImage("aux.aircondition.hc1.png", png),
            new SeedLineFixture { Model = "ows.heater.pdeh1a", Name = "无图型号", Category = "环境电器" },
            new SeedLineFixture { Model = "midjd.fridge.bs42s", Name = "已下架冰箱", Category = "厨房电器", IsAvailable = false },
        ]);

        var result = await CreateImporter().ImportAsync(package);

        Assert.Equal("2026-09-28", result.SnapshotDate);
        Assert.Equal(5, result.ProductsTotal);
        Assert.Equal(5, result.ProductsAdded);
        Assert.Equal(0, result.ProductsUpdated);
        Assert.Equal(3, result.ImageFilesTotal);
        Assert.Equal(3, result.ImagesCopied);

        await using var context = host.CreateContext();
        var products = new ProductRepository(context);
        Assert.Equal(5, await products.CountAsync());

        var heater = await products.GetByModelAsync("zhimi.heater.za1");
        Assert.NotNull(heater);
        Assert.Equal("米家智能电暖器", heater.Name);
        Assert.Equal("images/zhimi.heater.za1.png", heater.ImagePath);
        Assert.True(heater.IsAvailable);
        Assert.True(File.Exists(Path.Combine(host.Paths.ImagesDirectory, "zhimi.heater.za1.old.png")));

        var camera = await products.GetByModelAsync("tuyun.camera.abc");
        Assert.Equal("「涂鸦」摄像机, 高清版", camera!.Name);
        Assert.Equal(ImageFormat.Jpg, camera.ImageFormat);

        var aux = await products.GetByModelAsync("aux.aircondition.hc1");
        Assert.Equal("aux.aircondition.hc1.png", aux!.ImageFileName);
        Assert.Null(aux.ImagePath);
        Assert.Null(aux.Sha256);

        var noImage = await products.GetByModelAsync("ows.heater.pdeh1a");
        Assert.Null(noImage!.ImageFileName);
        Assert.Null(noImage.ImagePath);

        var delisted = await products.GetByModelAsync("midjd.fridge.bs42s");
        Assert.False(delisted!.IsAvailable);

        var state = await context.SyncState.AsNoTracking().SingleAsync(s => s.Id == 1);
        Assert.Equal("2026-09-28", state.SnapshotDate);
        Assert.Equal(SyncStatus.Idle, state.Status);
    }

    [Fact]
    public async Task Import_Twice_IsIdempotent()
    {
        var package = await WriteSmallPackageAsync();

        var first = await CreateImporter().ImportAsync(package);
        var second = await CreateImporter().ImportAsync(package);

        Assert.Equal(2, first.ProductsAdded);
        Assert.Equal(0, second.ProductsAdded);
        Assert.Equal(2, second.ProductsUpdated);
        Assert.Equal(first.ImageFilesTotal, second.ImagesSkipped);
        Assert.Equal(0, second.ImagesCopied);
        await using var context = host.CreateContext();
        Assert.Equal(2, await context.Products.CountAsync());
    }

    [Fact]
    public async Task Import_NeverTouchesUserData()
    {
        var package = await WriteSmallPackageAsync();
        var importer = CreateImporter();
        await importer.ImportAsync(package);

        await using var context = host.CreateContext();
        var product = await context.Products.AsNoTracking().SingleAsync(p => p.Model == "a.model.01");
        var favorites = new FavoritesRepository(context);
        var usages = new UsageRepository(context);
        await favorites.AddAsync(product.Id, 1_700_000_000);
        await usages.RecordAsync(product.Id, UsageType.Copy, 1_700_000_100);

        await importer.ImportAsync(package);

        Assert.Equal(1, await context.Favorites.CountAsync());
        var usage = await context.ProductUsages.AsNoTracking().SingleAsync();
        Assert.Equal(1, usage.CopyCount);
        Assert.Equal(0, await context.CollectionItems.CountAsync());
    }

    [Fact]
    public async Task Import_CorruptedImageContent_ThrowsWithShaMismatch()
    {
        var good = ImageFixtures.CreatePng();
        var corrupted = ImageFixtures.CreatePng(8, 8);
        var package = await SeedPackFixture.WriteAsync(TempDirectory(), "2026-09-28",
        [
            new SeedLineFixture { Model = "a.model.01", Name = "甲", Category = "其他" }
                .WithImage("a.model.01.png", good),
        ], zip =>
        {
            zip.GetEntry("images/a.model.01.png")!.Delete();
            var entry = zip.CreateEntry("images/a.model.01.png");
            using var stream = entry.Open();
            stream.Write(corrupted);
        });

        await Assert.ThrowsAsync<SeedPackageException>(() => CreateImporter().ImportAsync(package));

        await using var context = host.CreateContext();
        Assert.Equal(0, await context.Products.CountAsync());
    }

    [Fact]
    public async Task Import_MissingImageEntry_Throws()
    {
        var package = await SeedPackFixture.WriteAsync(TempDirectory(), "2026-09-28",
        [
            new SeedLineFixture { Model = "a.model.01", Name = "甲", Category = "其他" }
                .WithImage("a.model.01.png", ImageFixtures.CreatePng()),
        ], zip => zip.GetEntry("images/a.model.01.png")!.Delete());

        var exception = await Assert.ThrowsAsync<SeedPackageException>(() => CreateImporter().ImportAsync(package));
        Assert.Contains("缺少登记图片", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_ExtraImageEntry_Throws()
    {
        var package = await SeedPackFixture.WriteAsync(TempDirectory(), "2026-09-28",
        [
            new SeedLineFixture { Model = "a.model.01", Name = "甲", Category = "其他" }
                .WithImage("a.model.01.png", ImageFixtures.CreatePng()),
        ], zip =>
        {
            var entry = zip.CreateEntry("images/orphan.model.9.png");
            using var stream = entry.Open();
            stream.Write(ImageFixtures.CreatePng(4, 4));
        });

        var exception = await Assert.ThrowsAsync<SeedPackageException>(() => CreateImporter().ImportAsync(package));
        Assert.Contains("未登记图片", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_ManifestProductCountMismatch_Throws()
    {
        var package = await SeedPackFixture.WriteAsync(TempDirectory(), "2026-09-28",
        [
            new SeedLineFixture { Model = "a.model.01", Name = "甲", Category = "其他" }
                .WithImage("a.model.01.png", ImageFixtures.CreatePng()),
        ], zip =>
        {
            var entry = zip.GetEntry("manifest.json")!;
            entry.Delete();
            var rewritten = zip.CreateEntry("manifest.json");
            using var stream = rewritten.Open();
            var text = """
                {"schemaVersion":1,"snapshotDate":"2026-09-28","source":"x","productCount":99,"imageCount":1,"imageFileCount":1,"productsSha256":"mismatch","generator":"t"}
                """;
            stream.Write(Encoding.UTF8.GetBytes(text));
        });

        await Assert.ThrowsAsync<SeedPackageException>(() => CreateImporter().ImportAsync(package));
    }

    [Fact]
    public async Task Import_ProductsJsonlMissingField_ThrowsWithLineNumber()
    {
        var temp = TempDirectory();
        Directory.CreateDirectory(temp);
        var package = Path.Combine(temp, "seed-bad.zip");
        await using (var stream = File.Create(package))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var jsonl = Encoding.UTF8.GetBytes("""{"model":"a.model.01","brand":"b","category":"c"}""");
            var productsEntry = zip.CreateEntry("products.jsonl");
            using (var target = productsEntry.Open())
            {
                target.Write(jsonl);
            }

            var manifest = Encoding.UTF8.GetBytes(
                $$"""{"schemaVersion":1,"snapshotDate":"2026-09-28","source":"x","productCount":1,"imageCount":0,"imageFileCount":0,"productsSha256":"{{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(jsonl)).ToLowerInvariant()}}","generator":"t"}""");
            var manifestEntry = zip.CreateEntry("manifest.json");
            using (var target = manifestEntry.Open())
            {
                target.Write(manifest);
            }
        }

        var exception = await Assert.ThrowsAsync<SeedPackageException>(() => CreateImporter().ImportAsync(package));
        Assert.Contains("第 1 行", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_CancelledMidWay_DatabaseUntouched_ThenReimportSucceeds()
    {
        var bytes = ImageFixtures.CreatePng();
        var lines = new List<SeedLineFixture>();
        for (var i = 0; i < 6; i++)
        {
            lines.Add(new SeedLineFixture
                {
                    Model = $"a.model.{i:00}",
                    Name = $"产品 {i}",
                    Category = "其他",
                }
                .WithImage($"a.model.{i:00}.png", bytes));
        }

        var package = await SeedPackFixture.WriteAsync(TempDirectory(), "2026-09-28", lines);
        using var canceller = new CancellationTokenSource();

        canceller.Cancel();
        Exception? caught = null;
        try
        {
            await CreateImporter().ImportAsync(package, null, canceller.Token);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        await using var context = host.CreateContext();
        Assert.Equal(0, await context.Products.CountAsync());

        var recovered = await CreateImporter().ImportAsync(package);
        Assert.Equal(6, recovered.ProductsAdded);
        Assert.Equal(6, recovered.ProductsTotal);
    }

    [Fact]
    public async Task Import_RejectsSnapshotOlderThanLocal()
    {
        var package = await WriteSmallPackageAsync();
        await using (var context = host.CreateContext())
        {
            context.Products.Add(new Product
            {
                Model = "existing.model.01",
                Name = "n",
                Brand = "b",
                Category = "c",
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            });
            context.SyncState.Add(new SyncState { Id = 1, SnapshotDate = "2026-10-01" });
            await context.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<SeedPackageException>(() => CreateImporter().ImportAsync(package));
        Assert.Contains("拒绝降级导入", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_FixesCorruptedLocalImage()
    {
        var good = ImageFixtures.CreatePng();
        var package = await WriteSmallPackageAsync("a.model.01", good);
        var diskPath = Path.Combine(host.Paths.ImagesDirectory, "a.model.01.png");
        Directory.CreateDirectory(host.Paths.ImagesDirectory);
        await File.WriteAllBytesAsync(diskPath, "corrupted bytes"u8.ToArray());

        var result = await CreateImporter().ImportAsync(package);

        Assert.Equal(1, result.ImagesRepaired);
        Assert.Equal(good, await File.ReadAllBytesAsync(diskPath));
    }

    [Fact]
    public async Task SeedThenSync_ImageChanged_ClassifiedAsImageChanged_NotIdReuse()
    {
        var v1 = ImageFixtures.CreatePng(64, 32);
        var v2 = ImageFixtures.CreatePng(32, 64);
        var package = await SeedPackFixture.WriteAsync(TempDirectory(), "2026-09-28",
        [
            new SeedLineFixture { Model = "zhimi.heater.za1", Name = "米家智能电暖器", Category = "环境电器" }
                .WithImage("zhimi.heater.za1.png", v1),
            new SeedLineFixture { Model = "old.model.02", Name = "下架候选", Category = "其他" },
        ]);
        await CreateImporter().ImportAsync(package);

        var api = new FakeBaikeApiClient();
        api.AddCategory(7, "环境电器");
        api.AddProduct(7, "zhimi.heater.za1", "米家智能电暖器", "小米出品", 1_500_000_000, 1_700_000_200);
        api.AddCategory(9, "其他");
        var downloader = new FakeImageDownloader();
        downloader.Responses[$"https://cdn.cnbj1.fds.api.mi-img.com/iotweb-product-center/zhimi.heater.za1.png"] = v2;
        var engine = new SyncEngine(
            api,
            downloader,
            new ImageStore(host.Paths),
            new ThumbnailService(host.Paths),
            new ProductRepository(host.CreateContext()),
            new SyncStateRepository(host.CreateContext()));

        var run = await engine.SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, run.Status);
        Assert.Equal(1, run.Counts!.ImageChangedCount);
        Assert.Equal(0, run.Counts.IdReusedCount);
        Assert.Equal(1, run.Counts.DelistedCount);

        await using var context = host.CreateContext();
        var row = await context.Products.AsNoTracking().SingleAsync(p => p.Model == "zhimi.heater.za1");
        Assert.Equal(v2, await File.ReadAllBytesAsync(Path.Combine(host.Paths.ImagesDirectory, "zhimi.heater.za1.png")));
        Assert.NotNull(row.UpdateTimeUnix);
        Assert.Equal(1_700_000_200, row.UpdateTimeUnix);

        var api2 = new FakeBaikeApiClient();
        api2.AddCategory(7, "环境电器");
        api2.AddProduct(7, "zhimi.heater.za1", "米家智能电暖器", "小米出品", 1_500_000_000, 1_700_000_200);
        var downloader2 = new FakeImageDownloader();
        downloader2.Responses[downloader.Responses.Keys.First()] = v2;
        var callsBefore = downloader2.CallCount;
        var engine2 = new SyncEngine(
            api2,
            downloader2,
            new ImageStore(host.Paths),
            new ThumbnailService(host.Paths),
            new ProductRepository(host.CreateContext()),
            new SyncStateRepository(host.CreateContext()));
        await engine2.SyncNowAsync(SyncTrigger.Manual);
        Assert.Equal(0, downloader2.CallCount);
    }

    public void Dispose()
    {
        host.Dispose();
    }

    private async Task<string> WriteSmallPackageAsync(string model = "a.model.01", byte[]? bytes = null)
    {
        return await SeedPackFixture.WriteAsync(TempDirectory(), "2026-09-28",
        [
            new SeedLineFixture { Model = model, Name = "测试产品", Category = "其他" }
                .WithImage($"{model}.png", bytes ?? ImageFixtures.CreatePng()),
            new SeedLineFixture { Model = "b.model.02", Name = "第二产品", Category = "其他" },
        ]);
    }

    private string TempDirectory()
    {
        var directory = Path.Combine(host.Paths.Root, "seedtemp");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
