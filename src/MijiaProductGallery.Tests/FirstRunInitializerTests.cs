using System.IO.Compression;
using System.Net.Http;
using System.Text;
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

/// <summary>首次初始化测试：种子优先、在线兜底、断网显式失败态、已初始化短路。</summary>
public sealed class FirstRunInitializerTests : IDisposable, IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private readonly FakeBaikeApiClient api = new();
    private readonly FakeImageDownloader downloader = new();

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<InitializationReport> RunAsync(string? explicitSeed = null)
    {
        await using var context = host.CreateContext();
        var initializer = new FirstRunInitializer(
            host.Paths,
            new DbInitializer(context, host.Paths),
            context,
            new SeedImporter(context, host.Paths),
            api,
            new SyncEngine(
                api,
                downloader,
                new ImageStore(host.Paths),
                new ThumbnailService(host.Paths),
                new ProductRepository(host.CreateContext()),
                new SyncStateRepository(host.CreateContext())));
        return await initializer.InitializeAsync(explicitSeed);
    }

    [Fact]
    public async Task NoDbNoSeed_NoNetwork_ReturnsExplicitFailureState()
    {
        api.CategoriesError = new HttpRequestException("无法连接远程服务器");

        var report = await RunAsync();

        Assert.Equal(InitializationOutcome.FailedNoSeedOffline, report.Outcome);
        Assert.NotNull(report.Message);
        Assert.Contains("retry", report.AvailableActions);
        Assert.Contains("check-seed", report.AvailableActions);
        Assert.Contains("online-init", report.AvailableActions);
        await using var context = host.CreateContext();
        Assert.Equal(0, context.Products.Count());
    }

    [Fact]
    public async Task SeedInSeedDirectory_ImportsAndCompletes_Offline()
    {
        var package = await SeedPackFixture.WriteAsync(host.Paths.SeedDirectory, "2026-09-28",
        [
            new SeedLineFixture { Model = "zhimi.heater.za1", Name = "米家智能电暖器", Category = "环境电器" }
                .WithImage("zhimi.heater.za1.png", ImageFixtures.CreatePng()),
        ]);
        api.CategoriesError = new HttpRequestException("断网环境");

        var report = await RunAsync();

        Assert.Equal(InitializationOutcome.CompletedFromSeed, report.Outcome);
        Assert.Equal("2026-09-28", report.SnapshotDate);
        Assert.NotNull(report.SeedResult);
        await using var context = host.CreateContext();
        Assert.Equal(1, context.Products.Count());
    }

    [Fact]
    public async Task AlreadyInitialized_ShortCircuits()
    {
        var package = await SeedPackFixture.WriteAsync(host.Paths.SeedDirectory, "2026-09-28",
        [
            new SeedLineFixture { Model = "a.model.01", Name = "甲", Category = "其他" },
        ]);
        var first = await RunAsync();
        Assert.Equal(InitializationOutcome.CompletedFromSeed, first.Outcome);

        var second = await RunAsync();

        Assert.Equal(InitializationOutcome.AlreadyInitialized, second.Outcome);
        Assert.Equal("2026-09-28", second.SnapshotDate);
    }

    [Fact]
    public async Task NoSeed_NetworkAvailable_CompletesOnline()
    {
        api.AddCategory(8, "运动健康");
        api.AddProduct(8, "miwu.band.pro", "米家手环 Pro", "小米出品", 1_700_000_000, 1_700_000_100);
        downloader.Responses["https://cdn.cnbj1.fds.api.mi-img.com/iotweb-product-center/miwu.band.pro.png"] =
            ImageFixtures.CreatePng();

        var report = await RunAsync();

        Assert.Equal(InitializationOutcome.CompletedOnline, report.Outcome);
        await using var context = host.CreateContext();
        Assert.Equal(1, context.Products.Count());
    }

    [Fact]
    public async Task CorruptedSeedPackage_ReturnsFailedImport()
    {
        Directory.CreateDirectory(host.Paths.SeedDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(host.Paths.SeedDirectory, "seed-2026-09-28.zip"),
            "不是一个 zip 文件"u8.ToArray());

        var report = await RunAsync();

        Assert.Equal(InitializationOutcome.FailedImport, report.Outcome);
        Assert.NotNull(report.Message);
        Assert.Contains("check-seed", report.AvailableActions);
    }

    public void Dispose()
    {
        host.Dispose();
    }
}
