using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>同步状态与留痕仓储。状态恒写 Id=1 单行；运行结束用 ExecuteUpdate 收口。</summary>
public sealed class SyncStateRepository : ISyncStateRepository
{
    private readonly GalleryDbContext? shared;
    private readonly IDbContextFactory<GalleryDbContext>? factory;

    public SyncStateRepository(GalleryDbContext context) => shared = context;

    public SyncStateRepository(IDbContextFactory<GalleryDbContext> factory) => this.factory = factory;

    private DbContextScope Rent() => new(factory, shared);

    public async Task<SyncState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        var state = await scope.Context.SyncState.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
        return state ?? new SyncState { Id = 1 };
    }

    public async Task SaveStateAsync(SyncState state, CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        var context = scope.Context;
        var persisted = await context.SyncState.FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
        if (persisted is null)
        {
            context.SyncState.Add(state with { Id = 1 });
        }
        else
        {
            context.SyncState.Remove(persisted);
            context.SyncState.Add(state with { Id = 1 });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<long> StartRunAsync(SyncTrigger trigger, long startedUnix, CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        var run = new SyncRun
        {
            StartedUnix = startedUnix,
            Status = SyncStatus.Running,
            Stage = SyncStage.NotStarted,
            Trigger = trigger,
        };
        scope.Context.SyncRuns.Add(run);
        await scope.Context.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    public async Task CompleteRunAsync(
        long runId,
        SyncStatus status,
        SyncStage stage,
        SyncRunCounts? counts,
        string? errorMessage,
        long endedUnix,
        CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        await scope.Context.SyncRuns
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.Stage, stage)
                .SetProperty(r => r.EndedUnix, endedUnix)
                .SetProperty(r => r.Counts, counts)
                .SetProperty(r => r.ErrorMessage, errorMessage), cancellationToken);
    }

    public async Task<SyncRun?> GetRunAsync(long runId, CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        return await scope.Context.SyncRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
    }

    public async Task<SyncRun?> GetLatestRunAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        return await scope.Context.SyncRuns.AsNoTracking()
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddChangesAsync(long runId, IReadOnlyList<ProductChange> changes, CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        var context = scope.Context;
        foreach (var change in changes)
        {
            context.SyncChanges.Add(new SyncChange
            {
                SyncRunId = runId,
                Model = change.Model,
                Type = change.Type,
                Change = change,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
