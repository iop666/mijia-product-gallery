using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>
/// 使用统计服务：今日/本周/本月/累计计数与最常使用产品。
/// 边界按本地自然日/自然周（周一起算）/自然月计算；时钟经注入以便测试。
/// </summary>
public interface IUsageStatisticsService
{
    /// <summary>计算当前统计快照。</summary>
    Task<UsageStatistics> GetStatisticsAsync(int topCount = 5, CancellationToken cancellationToken = default);
}
