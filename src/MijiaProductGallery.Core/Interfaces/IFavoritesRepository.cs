namespace MijiaProductGallery.Core.Interfaces;

/// <summary>收藏仓储（用户数据）。</summary>
public interface IFavoritesRepository
{
    Task AddAsync(int productId, long createdUnix, CancellationToken cancellationToken = default);

    Task RemoveAsync(int productId, CancellationToken cancellationToken = default);

    Task<bool> IsFavoriteAsync(int productId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> GetFavoriteProductIdsAsync(CancellationToken cancellationToken = default);
}
