using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Images;
using MijiaProductGallery.Infrastructure.Sync;
using MijiaProductGallery.Tests.TestSupport;

namespace MijiaProductGallery.Tests;

/// <summary>临时目录断言依赖全局 %TEMP% 状态，禁止与其他测试集合并行。</summary>
[CollectionDefinition("SyncTempCleanup", DisableParallelization = true)]
public sealed class SyncTempCleanupCollection
{
}

/// <summary>同步临时目录生命周期：正常/取消后清理，启动清扫只回收无进程持有的孤儿目录。</summary>
[Collection("SyncTempCleanup")]
public sealed class SyncTempCleanupTests : IDisposable
{
    private readonly SyncTestHost host = new();

    [Fact]
    public async Task SuccessfulSync_RemovesItsTempDirectory()
    {
        var before = SyncDirectories();
        host.Api.AddCategory(8, "运动健康");
        host.Api.AddProduct(8, "miwu.band.pro", "米家手环 Pro", "小米出品", 1_700_000_000, 1_700_000_100);
        host.Downloader.Responses[IconUrl("miwu.band.pro")] = ImageFixtures.CreatePng();

        var run = await (await host.CreateEngineAsync()).SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, run.Status);
        Assert.True(SyncDirectories().SetEquals(before));
    }

    [Fact]
    public async Task CancelledSync_RemovesItsTempDirectory()
    {
        await host.EnsureInitializedAsync();
        host.Api.AddCategory(8, "运动健康");
        host.Api.AddProduct(8, "miwu.band.pro", "米家手环 Pro", "小米出品", 1_700_000_000, 1_700_000_100);
        var downloader = new BlockingImageDownloader();
        var engine = new SyncEngine(
            host.Api,
            downloader,
            new ImageStore(host.Database.Paths),
            new ThumbnailService(host.Database.Paths),
            new ProductRepository(host.Database.CreateContext()),
            new SyncStateRepository(host.Database.CreateContext()));
        var before = SyncDirectories();

        using var cts = new CancellationTokenSource();
        var sync = engine.SyncNowAsync(SyncTrigger.Manual, cts.Token);
        await downloader.Started.WaitAsync(TimeSpan.FromSeconds(10));
        var tempDirectory = Assert.Single(SyncDirectories().Except(before));

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sync);
        Assert.False(Directory.Exists(tempDirectory));
    }

    [Fact]
    public void CleanupOrphans_RemovesDeadAndOldLegacyDirectories_KeepsLiveAndRecent()
    {
        var root = Path.Combine(Path.GetTempPath(), "mpg-sync-cleanup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var liveLock = new FileStream(
            Path.Combine(CreateDeadDirectory(root, "sync-live"), ".sync.lock"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        try
        {
            var dead = CreateDeadDirectory(root, "sync-dead");
            var legacyOld = CreateLegacyDirectory(root, "sync-legacy-old", DateTime.UtcNow.AddHours(-2));
            var legacyRecent = CreateLegacyDirectory(root, "sync-legacy-new", DateTime.UtcNow);

            var removed = SyncTempDirectory.CleanupOrphans(root);

            Assert.Equal(2, removed);
            Assert.False(Directory.Exists(dead));
            Assert.False(Directory.Exists(legacyOld));
            Assert.True(Directory.Exists(Path.Combine(root, "sync-live")));
            Assert.True(Directory.Exists(legacyRecent));
        }
        finally
        {
            liveLock.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    public void Dispose()
    {
        host.Dispose();
    }

    /// <summary>当前真实临时根下的 sync-* 目录（全路径）。</summary>
    private static HashSet<string> SyncDirectories()
    {
        return Directory.Exists(SyncTempDirectory.RootDirectory)
            ? new HashSet<string>(Directory.GetDirectories(SyncTempDirectory.RootDirectory, "sync-*"))
            : [];
    }

    /// <summary>有锁文件但无人持有的目录（进程被强杀后的形态）。</summary>
    private static string CreateDeadDirectory(string root, string name)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        using (File.Create(Path.Combine(path, ".sync.lock")))
        {
        }

        return path;
    }

    /// <summary>无锁文件的旧版本残留目录，按最后写入时间判定是否过期。</summary>
    private static string CreateLegacyDirectory(string root, string name, DateTime lastWriteUtc)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        var file = Path.Combine(path, "image");
        File.WriteAllText(file, "x");
        File.SetLastWriteTimeUtc(file, lastWriteUtc);
        Directory.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    private static string IconUrl(string model)
    {
        return $"https://cdn.cnbj1.fds.api.mi-img.com/iotweb-product-center/{model}.png";
    }

    /// <summary>阻塞到取消的下载桩，用于在同步中制造中断。</summary>
    private sealed class BlockingImageDownloader : IImageDownloader
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }
    }
}
