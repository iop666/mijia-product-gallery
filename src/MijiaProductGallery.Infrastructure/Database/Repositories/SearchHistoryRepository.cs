using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>搜索历史仓储（用户数据）。同查询合并刷新，超过上限删除最旧。</summary>
public sealed class SearchHistoryRepository(GalleryDbContext context) : ISearchHistoryRepository
{
    public async Task AddAsync(string query, long createdUnix, int resultCount, CancellationToken cancellationToken = default)
    {
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
        await TrimToLimitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SearchHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        return await context.SearchHistories
            .AsNoTracking()
            .OrderByDescending(h => h.CreatedUnix)
            .ThenByDescending(h => h.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await context.SearchHistories.ExecuteDeleteAsync(cancellationToken);
    }

    private async Task TrimToLimitAsync(CancellationToken cancellationToken)
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
