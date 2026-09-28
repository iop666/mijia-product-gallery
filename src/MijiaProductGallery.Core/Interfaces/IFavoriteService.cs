using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>
/// 收藏服务契约：全部操作幂等（重复添加/重复移除结果一致）。
/// 收藏是用户数据；同步引擎禁止访问本接口与 Favorites 表。
/// </summary>
public interface IFavoriteService
{
    /// <summary>该产品是否已收藏（产品无收藏记录返回 false）。</summary>
    Task<bool> IsFavoriteAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>添加收藏；已收藏则为无操作（幂等）。</summary>
    Task AddAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>移除收藏；未收藏则为无操作（幂等）。</summary>
    Task RemoveAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>切换收藏状态，返回切换后的状态（true=已收藏）。</summary>
    Task<bool> ToggleAsync(int productId, CancellationToken cancellationToken = default);
}
