using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>搜索历史仓储（用户数据，查询去重、重复搜索刷新时间与结果数，保留最近 30 条）。</summary>
public interface ISearchHistoryRepository
{
    /// <summary>搜索历史上限。</summary>
    public const int MaxEntries = 30;

    /// <summary>记录一次搜索：同查询合并为一条并刷新时间与结果数；随后裁剪至最近 30 条。</summary>
    Task AddAsync(string query, long createdUnix, int resultCount, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
