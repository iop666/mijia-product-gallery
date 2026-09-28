using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>同步服务契约（由同步引擎实现，后台执行，不阻塞 UI）。</summary>
public interface ISyncService
{
    /// <summary>立即执行一轮全量对比同步；同轮已在进行时返回进行中的那一轮。</summary>
    Task<SyncRun> SyncNowAsync(SyncTrigger trigger, CancellationToken cancellationToken = default);
}
