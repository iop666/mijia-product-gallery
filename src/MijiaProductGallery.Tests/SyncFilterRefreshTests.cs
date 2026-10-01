using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Query;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Infrastructure.Images;
using MijiaProductGallery.Tests.Database;
using MijiaProductGallery.Tests.TestSupport;
using MijiaProductGallery.ViewModels;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>
/// 同步完成后的图库刷新：有数据变化的终态重建筛选可选项（分类/品牌）并重查；
/// 运行中事件与零变化终态不扰动当前视图。
/// </summary>
public sealed class SyncFilterRefreshTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private readonly FakeSyncService sync = new();

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
    public async Task SyncWithChanges_ReloadsFilterOptions_AndRequeries()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();
        Assert.Empty(vm.FilterPane.Categories);
        Assert.Empty(vm.FilterPane.Brands);

        await new ProductRepository(host.CreateContext()).AddRangeAsync(
        [
            new Product
            {
                Model = "miwu.scale.s400",
                Name = "米家体脂秤 S400",
                Brand = "小米出品",
                Category = "厨房电器",
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            },
            new Product
            {
                Model = "chuangmi.camera.029a02",
                Name = "小米智能摄像机",
                Brand = "云米",
                Category = "安防",
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            },
        ]);

        sync.Raise(new SyncProgress
        {
            Status = SyncStatus.Success,
            Stage = SyncStage.Completed,
            StageIndex = SyncProgress.TotalStages + 1,
            OverallPercent = 100,
            Counts = new SyncRunCounts { NewCount = 2 },
        });

        await WaitUntilAsync(() => vm.FilterPane.Categories.Count == 2);
        Assert.Equal(
            ["厨房电器", "安防"],
            vm.FilterPane.Categories.Select(option => option.Label).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(
            ["云米", "小米出品"],
            vm.FilterPane.Brands.Select(option => option.Label).Order(StringComparer.Ordinal).ToList());

        await WaitUntilAsync(() => vm.Cards.Count == 2);
        Assert.Equal(2, vm.TotalCount);
        Assert.Equal(GalleryLoadState.Ready, vm.State);
    }

    [Fact]
    public async Task RunningProgress_DoesNotTriggerRefresh()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();

        sync.Raise(new SyncProgress
        {
            Status = SyncStatus.Running,
            Stage = SyncStage.DownloadingImages,
            StageIndex = 4,
            Done = 5,
            Total = 10,
            ElapsedSeconds = 10,
        });

        await Task.Delay(100);
        Assert.Empty(vm.FilterPane.Categories);
    }

    [Fact]
    public async Task TerminalWithoutChanges_DoesNotTriggerRefresh()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();
        Assert.Empty(vm.FilterPane.Categories);

        sync.Raise(new SyncProgress
        {
            Status = SyncStatus.Success,
            Stage = SyncStage.Completed,
            StageIndex = SyncProgress.TotalStages + 1,
            OverallPercent = 100,
            Counts = new SyncRunCounts(),
        });

        await Task.Delay(100);
        Assert.Empty(vm.FilterPane.Categories);
        Assert.Equal(GalleryLoadState.Empty, vm.State);
    }

    private GalleryViewModel CreateViewModel()
    {
        var factory = new TestDbContextFactory(() => host.CreateContext());
        return new GalleryViewModel(
            new ProductRepository(host.CreateContext()),
            new ThumbnailLoadQueue(
                new ThumbnailService(host.Paths),
                InlineUiDispatcher.Instance,
                concurrency: 1),
            new ProductQueryService(factory, new FilterService(), new SortService()),
            new RecentService(host.CreateContext()),
            new SearchHistoryRepository(host.CreateContext()),
            new FavoritesRepository(host.CreateContext()),
            new InMemorySettings(),
            InlineUiDispatcher.Instance,
            10,
            null,
            sync);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 5_000)
    {
        for (var waited = 0; waited < timeoutMilliseconds; waited += 20)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(condition(), "等待条件超时");
    }

    private sealed class FakeSyncService : ISyncService
    {
        public event Action<SyncProgress>? ProgressChanged;

        public Task<SyncRun> SyncNowAsync(SyncTrigger trigger, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("测试不触发真实同步");
        }

        public Task CancelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Raise(SyncProgress progress) => ProgressChanged?.Invoke(progress);
    }
}
