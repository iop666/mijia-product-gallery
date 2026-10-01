using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Tests.Database;
using MijiaProductGallery.Tests.TestSupport;
using MijiaProductGallery.ViewModels;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>同步中心实时进度：引擎进度事件驱动运行态/百分比/明细，终态复位进度条。</summary>
public sealed class SyncCenterProgressTests : IAsyncLifetime
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

    private SyncCenterViewModel CreateViewModel()
    {
        return new SyncCenterViewModel(
            sync,
            new SyncStateRepository(host.CreateContext()),
            new InMemorySettings(),
            InlineUiDispatcher.Instance);
    }

    [Fact]
    public void RunningProgress_UpdatesState_Percent_AndDetail()
    {
        var vm = CreateViewModel();

        sync.Raise(new SyncProgress
        {
            Status = SyncStatus.Running,
            Stage = SyncStage.DownloadingImages,
            StageIndex = 4,
            Detail = "1,234/10,532",
            Done = 1_234,
            Total = 10_532,
            ElapsedSeconds = 60,
            OverallPercent = 22,
            Failures = 3,
        });

        Assert.True(vm.IsRunning);
        Assert.Equal("同步中", vm.StatusText);
        Assert.Equal("下载图片", vm.StageText);
        Assert.StartsWith("阶段 4/6", vm.ProgressStageText);
        Assert.False(vm.IsProgressIndeterminate);
        Assert.Equal(22, vm.ProgressPercent);
        Assert.Equal("22%", vm.ProgressPercentText);
        Assert.Contains("1,234/10,532", vm.ProgressDetailText);
        Assert.Contains("已用 01:00", vm.ProgressDetailText);
        Assert.Contains("剩余约", vm.ProgressDetailText);
        Assert.Contains("图片失败 3", vm.ProgressDetailText);
    }

    [Fact]
    public void RunningProgress_WithoutTotal_IsIndeterminate()
    {
        var vm = CreateViewModel();

        sync.Raise(new SyncProgress
        {
            Status = SyncStatus.Running,
            Stage = SyncStage.Comparing,
            StageIndex = 3,
        });

        Assert.True(vm.IsRunning);
        Assert.True(vm.IsProgressIndeterminate);
        Assert.Equal(string.Empty, vm.ProgressPercentText);
    }

    [Fact]
    public void TerminalEvent_ResetsProgressBar_Form()
    {
        var vm = CreateViewModel();
        sync.Raise(new SyncProgress
        {
            Status = SyncStatus.Running,
            Stage = SyncStage.DownloadingImages,
            StageIndex = 4,
            Done = 10,
            Total = 100,
            ElapsedSeconds = 5,
            OverallPercent = 20,
        });
        Assert.False(vm.IsProgressIndeterminate);

        sync.Raise(new SyncProgress
        {
            Status = SyncStatus.Success,
            Stage = SyncStage.Completed,
            StageIndex = SyncProgress.TotalStages + 1,
            OverallPercent = 100,
            Counts = new SyncRunCounts { NewCount = 5 },
        });

        Assert.False(vm.IsProgressIndeterminate);
        Assert.Equal(100, vm.ProgressPercent);
    }

    [Fact]
    public async Task RefreshAsync_DerivesRunningState_FromPersistedStatus()
    {
        await using (var context = host.CreateContext())
        {
            await new SyncStateRepository(context).SaveStateAsync(
                new SyncState { Id = 1, Status = SyncStatus.Running, Stage = SyncStage.FetchingProducts },
                CancellationToken.None);
        }

        var vm = CreateViewModel();
        await vm.RefreshAsync();

        Assert.True(vm.IsRunning);
        Assert.Equal("同步中", vm.StatusText);
        Assert.Equal("获取产品", vm.StageText);

        await using (var context = host.CreateContext())
        {
            await new SyncStateRepository(context).SaveStateAsync(
                new SyncState { Id = 1, Status = SyncStatus.Success, Stage = SyncStage.Completed },
                CancellationToken.None);
        }

        await vm.RefreshAsync();

        Assert.False(vm.IsRunning);
        Assert.Equal("成功", vm.StatusText);
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
