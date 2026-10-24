using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>使用计数与使用事件仓储（用户数据）。单次记录在事务内完成，失败即整体回滚。</summary>
public sealed class UsageRepository : IUsageRepository
{
    private readonly GalleryDbContext? shared;
    private readonly IDbContextFactory<GalleryDbContext>? factory;

    public UsageRepository(GalleryDbContext context) => shared = context;

    public UsageRepository(IDbContextFactory<GalleryDbContext> factory) => this.factory = factory;

    private DbContextScope Rent() => new(factory, shared);

    public async Task RecordAsync(int productId, UsageType type, long occurredUnix, CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        var context = scope.Context;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var usage = await context.ProductUsages.FirstOrDefaultAsync(u => u.ProductId == productId, cancellationToken);
        if (usage is null)
        {
            usage = new ProductUsage { ProductId = productId };
            context.ProductUsages.Add(usage);
        }

        switch (type)
        {
            case UsageType.View:
                usage.ViewCount++;
                break;
            case UsageType.Copy:
            case UsageType.CopyText:
                usage.CopyCount++;
                break;
            case UsageType.Drag:
                usage.DragCount++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        usage.TotalUseCount++;
        usage.LastUsedUnix = occurredUnix;

        context.UsageEvents.Add(new UsageEvent
        {
            ProductId = productId,
            Type = type,
            UsedUnix = occurredUnix,
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ProductUsage?> GetCountsAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        return await scope.Context.ProductUsages.FirstOrDefaultAsync(u => u.ProductId == productId, cancellationToken);
    }

    public async Task<IReadOnlyList<UsageEvent>> GetRecentEventsAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        return await scope.Context.UsageEvents
            .AsNoTracking()
            .OrderByDescending(e => e.UsedUnix)
            .ThenByDescending(e => e.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
