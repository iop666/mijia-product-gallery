using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Tests.Database;
using MijiaProductGallery.Infrastructure.Sync;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>自动同步调度测试：IsDue 纯函数与到期触发。</summary>
public sealed class AutoSyncSchedulerTests : IAsyncLifetime
{
    private const long Now = 1_800_000_000;

    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();
    private readonly RecordingSyncService syncService = new();
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

    [Theory]
    [InlineData(SyncAutoInterval.Off, null, false)]
    [InlineData(SyncAutoInterval.Off, 1_799_000_000L, false)]
    [InlineData(SyncAutoInterval.Startup, null, false)]
    [InlineData(SyncAutoInterval.Hours6, null, true)]
    [InlineData(SyncAutoInterval.Hours6, 1_800_000_000L - 6 * 3600L, true)]
    [InlineData(SyncAutoInterval.Hours6, 1_800_000_000L - 6 * 3600L + 1, false)]
    [InlineData(SyncAutoInterval.Daily, 1_800_000_000L - 24 * 3600L, true)]
    [InlineData(SyncAutoInterval.Daily, 1_800_000_000L - 24 * 3600L + 1, false)]
    [InlineData(SyncAutoInterval.Weekly, 1_800_000_000L - 7 * 24 * 3600L, true)]
    [InlineData(SyncAutoInterval.Weekly, 1_800_000_000L - 7 * 24 * 3600L + 1, false)]
    public void IsDue_FollowsIntervalSemantics(SyncAutoInterval interval, long? lastSuccessful, bool expected)
    {
        Assert.Equal(expected, AutoSyncScheduler.IsDue(interval, lastSuccessful, Now));
    }

    [Fact]
    public void IsDue_Startup_TriggersOnEveryStartupCheck()
    {
        // 「每次启动」= 每次应用启动都触发一次同步（忽略上次同步时间）。
        Assert.True(AutoSyncScheduler.IsDue(SyncAutoInterval.Startup, null, Now, isStartupCheck: true));
        Assert.True(AutoSyncScheduler.IsDue(SyncAutoInterval.Startup, 1_700_000_000L, Now, isStartupCheck: true));
    }

    [Fact]
    public void IsDue_Startup_PeriodicCheckNeverTriggers()
    {
        Assert.False(AutoSyncScheduler.IsDue(SyncAutoInterval.Startup, null, Now, isStartupCheck: false));
    }

    [Fact]
    public async Task CheckAndTrigger_WhenDue_TriggersScheduledSync()
    {
        // 从未成功同步：任何非 Off 间隔都到期。
        var settingsRepository = new SettingsRepository(host.CreateContext());
        await settingsRepository.SetValueAsync("Sync.AutoInterval", nameof(SyncAutoInterval.Daily));
        var scheduler = CreateScheduler();

        await scheduler.CheckAndTriggerAsync();

        Assert.Equal(1, syncService.SyncCallCount);
        Assert.Equal(SyncTrigger.Scheduled, syncService.LastTrigger);
    }

    [Fact]
    public async Task CheckAndTrigger_Off_NeverTriggers()
    {
        var settingsRepository = new SettingsRepository(host.CreateContext());
        await settingsRepository.SetValueAsync("Sync.AutoInterval", nameof(SyncAutoInterval.Off));
        var scheduler = CreateScheduler();

        await scheduler.CheckAndTriggerAsync();

        Assert.Equal(0, syncService.SyncCallCount);
    }

    [Fact]
    public async Task CheckAndTrigger_NotDue_DoesNotTrigger()
    {
        var settingsRepository = new SettingsRepository(host.CreateContext());
        await settingsRepository.SetValueAsync("Sync.AutoInterval", nameof(SyncAutoInterval.Daily));
        var stateRepository = new SyncStateRepository(host.CreateContext());
        await stateRepository.SaveStateAsync(
            new SyncState
            {
                Id = 1,
                Status = SyncStatus.Success,
                LastSuccessfulSyncUnix = Now - 3600,
            });

        var scheduler = CreateScheduler();
        await scheduler.CheckAndTriggerAsync();

        Assert.Equal(0, syncService.SyncCallCount);
    }

    private AutoSyncScheduler CreateScheduler()
    {
        var factory = new TestDbContextFactory(() => host.CreateContext());
        return new AutoSyncScheduler(
            syncService,
            factory,
            new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(Now)));
    }

    private sealed class RecordingSyncService : ISyncService
    {
        public int SyncCallCount { get; private set; }

        public SyncTrigger? LastTrigger { get; private set; }

        public Task<SyncRun> SyncNowAsync(SyncTrigger trigger, CancellationToken cancellationToken = default)
        {
            SyncCallCount++;
            LastTrigger = trigger;
            SyncRun run = new()
            {
                Id = SyncCallCount,
                StartedUnix = 1_800_000_000,
                Status = SyncStatus.Success,
                Stage = SyncStage.Completed,
                Trigger = trigger,
                Counts = new SyncRunCounts(),
            };
            return Task.FromResult(run);
        }

        public Task CancelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
