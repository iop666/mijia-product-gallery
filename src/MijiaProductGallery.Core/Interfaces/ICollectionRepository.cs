using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>收藏合集仓储（用户数据）。</summary>
public interface ICollectionRepository
{
    Task<Collection> CreateAsync(string name, long createdUnix, CancellationToken cancellationToken = default);

    Task RenameAsync(int collectionId, string newName, long updatedUnix, CancellationToken cancellationToken = default);

    Task DeleteAsync(int collectionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Collection>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddItemAsync(int collectionId, int productId, long addedUnix, CancellationToken cancellationToken = default);

    Task RemoveItemAsync(int collectionId, int productId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> GetProductIdsAsync(int collectionId, CancellationToken cancellationToken = default);
}
