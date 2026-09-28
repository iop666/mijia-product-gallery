namespace MijiaProductGallery.Core.Interfaces;

/// <summary>收藏仓储（用户数据）。所有操作幂等；外键级联于产品删除。</summary>
public interface IFavoritesRepository
{
    /// <summary>添加收藏；已收藏则为无操作（幂等）。</summary>
    Task AddAsync(int productId, long createdUnix, CancellationToken cancellationToken = default);

    /// <summary>移除收藏；未收藏则为无操作（幂等）。</summary>
    Task RemoveAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>切换收藏状态，返回切换后的状态（true=已收藏）。</summary>
    Task<bool> ToggleAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>该产品是否已收藏。</summary>
    Task<bool> IsFavoriteAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>全部收藏的产品 Id（按收藏时间升序）。</summary>
    Task<IReadOnlyList<int>> GetFavoriteProductIdsAsync(CancellationToken cancellationToken = default);
}
