using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>搜索历史仓储（用户数据）。同查询合并刷新，超过上限删除最旧。</summary>
public sealed class SearchHistoryRepository : ISearchHistoryRepository
{
    private readonly GalleryDbContext? shared;
    private readonly IDbContextFactory<GalleryDbContext>? factory;

    public SearchHistoryRepository(GalleryDbContext context) => shared = context;

    public SearchHistoryRepository(IDbContextFactory<GalleryDbContext> factory) => this.factory = factory;

    private DbContextScope Rent() => new(factory, shared);

    public async Task AddAsync(string query, long createdUnix, int resultCount, CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        var context = scope.Context;
        var existing = await context.SearchHistories
            .FirstOrDefaultAsync(h => h.Query == query, cancellationToken);
        if (existing is not null)
        {
            context.SearchHistories.Remove(existing);
        }

        context.SearchHistories.Add(new SearchHistoryEntry
        {
            Query = query,
            CreatedUnix = createdUnix,
            ResultCount = resultCount,
        });
        await context.SaveChangesAsync(cancellationToken);
        await TrimToLimitAsync(context, cancellationToken);
    }

    public async Task<IReadOnlyList<SearchHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        return await scope.Context.SearchHistories
            .AsNoTracking()
            .OrderByDescending(h => h.CreatedUnix)
            .ThenByDescending(h => h.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        await scope.Context.SearchHistories.ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task TrimToLimitAsync(GalleryDbContext context, CancellationToken cancellationToken)
    {
        var overflowIds = await context.SearchHistories
            .OrderByDescending(h => h.CreatedUnix)
            .ThenByDescending(h => h.Id)
            .Skip(ISearchHistoryRepository.MaxEntries)
            .Select(h => h.Id)
            .ToListAsync(cancellationToken);
        if (overflowIds.Count > 0)
        {
            await context.SearchHistories
                .Where(h => overflowIds.Contains(h.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
