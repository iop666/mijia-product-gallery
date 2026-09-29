using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>收藏合集仓储（用户数据）。删除合集随外键级联清理成员行。</summary>
public sealed class CollectionRepository(GalleryDbContext context) : ICollectionRepository
{
    public async Task<Collection> CreateAsync(string name, long createdUnix, CancellationToken cancellationToken = default)
    {
        var collection = new Collection { Name = name, CreatedUnix = createdUnix, UpdatedUnix = createdUnix };
        context.Collections.Add(collection);
        await context.SaveChangesAsync(cancellationToken);
        return collection;
    }

    public async Task RenameAsync(int collectionId, string newName, long updatedUnix, CancellationToken cancellationToken = default)
    {
        await context.Collections
            .Where(c => c.Id == collectionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.Name, newName)
                .SetProperty(c => c.UpdatedUnix, updatedUnix), cancellationToken);
    }

    public async Task DeleteAsync(int collectionId, CancellationToken cancellationToken = default)
    {
        await context.Collections
            .Where(c => c.Id == collectionId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Collection>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await context.Collections
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddItemAsync(int collectionId, int productId, long addedUnix, CancellationToken cancellationToken = default)
    {
        var exists = await context.CollectionItems.AnyAsync(
            i => i.CollectionId == collectionId && i.ProductId == productId,
            cancellationToken);
        if (exists)
        {
            return;
        }

        context.CollectionItems.Add(new CollectionItem
        {
            CollectionId = collectionId,
            ProductId = productId,
            AddedUnix = addedUnix,
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveItemAsync(int collectionId, int productId, CancellationToken cancellationToken = default)
    {
        await context.CollectionItems
            .Where(i => i.CollectionId == collectionId && i.ProductId == productId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<int>> GetProductIdsAsync(int collectionId, CancellationToken cancellationToken = default)
    {
        return await context.CollectionItems
            .Where(i => i.CollectionId == collectionId)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.AddedUnix)
            .Select(i => i.ProductId)
            .ToListAsync(cancellationToken);
    }
}
