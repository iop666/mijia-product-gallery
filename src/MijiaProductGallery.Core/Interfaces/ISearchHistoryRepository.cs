using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>搜索历史仓储（用户数据，查询去重、重复搜索刷新时间）。</summary>
public interface ISearchHistoryRepository
{
    Task AddAsync(string query, long createdUnix, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
