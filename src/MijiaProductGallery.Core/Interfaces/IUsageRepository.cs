using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>使用计数与使用事件仓储（用户数据）。</summary>
public interface IUsageRepository
{
    /// <summary>记录一次使用行为：累加对应计数（含 TotalUseCount、LastUsedUnix）并写入事件行，整体原子。</summary>
    Task RecordAsync(int productId, UsageType type, long occurredUnix, CancellationToken cancellationToken = default);

    Task<ProductUsage?> GetCountsAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>最近使用事件（含查看），按时间倒序。</summary>
    Task<IReadOnlyList<UsageEvent>> GetRecentEventsAsync(int limit, CancellationToken cancellationToken = default);
}
