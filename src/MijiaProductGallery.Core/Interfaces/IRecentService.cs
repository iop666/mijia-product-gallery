using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>
/// 最近使用视图服务：按 UsageEvents 最新事件时间倒序返回产品（数据库侧聚合），
/// 并提供清空历史（仅删除 UsageEvents 与 ProductUsages，不影响收藏与搜索历史）。
/// </summary>
public interface IRecentService
{
    /// <summary>最近使用的产品（按最近事件时间降序，同时间按型号稳定排序）。</summary>
    Task<IReadOnlyList<RecentProduct>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>清空使用历史：删除全部 UsageEvents 与 ProductUsages；Favorites/SearchHistories 不受影响。</summary>
    Task ClearHistoryAsync(CancellationToken cancellationToken = default);
}
