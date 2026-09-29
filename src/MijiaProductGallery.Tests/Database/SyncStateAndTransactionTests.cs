using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using Xunit;

namespace MijiaProductGallery.Tests.Database;

/// <summary>同步留痕仓储与事务回滚测试。</summary>
public sealed class SyncStateAndTransactionTests : IDisposable
{
    private readonly DatabaseTestHost _host = DatabaseTestHost.CreateNotInitialized();

    [Fact]
    public async Task SyncState_DefaultsToIdleSingleton_AndRoundTrips()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();
        var repository = new SyncStateRepository(context);

        var initial = await repository.GetStateAsync();
        Assert.Equal(SyncStatus.Idle, initial.Status);
        Assert.Equal(1, initial.Id);

        await repository.SaveStateAsync(new SyncState
        {
            Id = 1,
            LastSyncUnix = 1_700_000_000,
            LastSuccessfulSyncUnix = 1_700_000_100,
            Status = SyncStatus.Success,
            Stage = SyncStage.Completed,
            SnapshotDate = "2026-09-28",
        });

        var saved = await repository.GetStateAsync();
        Assert.Equal(SyncStatus.Success, saved.Status);
        Assert.Equal("2026-09-28", saved.SnapshotDate);
        Assert.Equal(1, await context.SyncState.CountAsync());
    }

    [Fact]
    public async Task SyncRunLifecycle_WritesCountsAndChanges_WithDetailJsonRoundTrip()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();
        var repository = new SyncStateRepository(context);

        var runId = await repository.StartRunAsync(SyncTrigger.Scheduled, 1_700_000_000);
        var change = new ProductChange
        {
            Type = ChangeType.IdReused,
            Model = "chuangmi.camera.029a02",
            OldName = "小米智能摄像机",
            NewName = "小米智能门锁",
            ImageOutcome = ImageOutcome.IdReused,
            ImageFileName = "chuangmi.camera.029a02.png",
            ReplacedByOldFileName = "chuangmi.camera.029a02.old.png",
        };
        await repository.AddChangesAsync(runId, [change]);
        await repository.CompleteRunAsync(
            runId,
            SyncStatus.Success,
            SyncStage.Completed,
            new SyncRunCounts { IdReusedCount = 1, ImageChangedCount = 2 },
            errorMessage: null,
            1_700_000_999);

        var run = Assert.Single(await context.SyncRuns.AsNoTracking().ToListAsync());
        Assert.Equal(SyncStatus.Success, run.Status);
        Assert.Equal(SyncStage.Completed, run.Stage);
        Assert.Equal(SyncTrigger.Scheduled, run.Trigger);
        Assert.NotNull(run.Counts);
        Assert.Equal(1, run.Counts.IdReusedCount);
        Assert.Equal(2, run.Counts.ImageChangedCount);

        var storedChange = Assert.Single(await context.SyncChanges.AsNoTracking().ToListAsync());
        Assert.Equal(ChangeType.IdReused, storedChange.Type);
        Assert.Equal("chuangmi.camera.029a02", storedChange.Model);
        Assert.Equal("小米智能摄像机", storedChange.Change.OldName);
        Assert.Equal("chuangmi.camera.029a02.old.png", storedChange.Change.ReplacedByOldFileName);
    }

    [Fact]
    public async Task UsageRecord_WithMissingProduct_FailsAtomically()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();
        var usages = new UsageRepository(context);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => usages.RecordAsync(9999, UsageType.Copy, 1_700_000_000));

        Assert.Equal(0, await context.ProductUsages.CountAsync());
        Assert.Equal(0, await context.UsageEvents.CountAsync());
    }

    [Fact]
    public async Task ExplicitTransaction_RollbackLeavesNothingBehind()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();

        await context.Database.BeginTransactionAsync();
        context.Products.Add(new Product
        {
            Model = "zhimi.heater.za1",
            Name = "米家智能电暖器",
            Brand = "小米出品",
            Category = "环境电器",
            FirstSeenUnix = 1,
            LastSeenUnix = 1,
        });
        await context.SaveChangesAsync();
        await context.Database.CurrentTransaction!.RollbackAsync();
        context.ChangeTracker.Clear();

        Assert.Equal(0, await context.Products.CountAsync());
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}
