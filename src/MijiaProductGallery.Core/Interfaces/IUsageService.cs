using MijiaProductGallery.Core.Enums;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>使用行为记录契约（由服务层实现；是否计数由应用设置决定）。</summary>
public interface IUsageService
{
    /// <summary>记录一次使用行为并累加对应计数（TotalUseCount 同步 +1）。</summary>
    Task RecordAsync(int productId, UsageType type, long occurredUnix, CancellationToken cancellationToken = default);
}
