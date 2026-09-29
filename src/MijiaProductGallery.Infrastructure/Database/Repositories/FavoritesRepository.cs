using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Interfaces;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>收藏仓储（用户数据）。</summary>
public sealed class FavoritesRepository(GalleryDbContext context) : IFavoritesRepository
{
    public async Task AddAsync(int productId, long createdUnix, CancellationToken cancellationToken = default)
    {
        var exists = await context.Favorites.AnyAsync(f => f.ProductId == productId, cancellationToken);
        if (exists)
        {
            return;
        }

        context.Favorites.Add(new Core.Models.FavoriteItem { ProductId = productId, CreatedUnix = createdUnix });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(int productId, CancellationToken cancellationToken = default)
    {
        await context.Favorites
            .Where(f => f.ProductId == productId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task<bool> IsFavoriteAsync(int productId, CancellationToken cancellationToken = default)
    {
        return context.Favorites.AnyAsync(f => f.ProductId == productId, cancellationToken);
    }

    public async Task<bool> ToggleAsync(int productId, CancellationToken cancellationToken = default)
    {
        if (await context.Favorites.AnyAsync(f => f.ProductId == productId, cancellationToken))
        {
            await RemoveAsync(productId, cancellationToken);
            return false;
        }

        await AddAsync(productId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<int>> GetFavoriteProductIdsAsync(CancellationToken cancellationToken = default)
    {
        return await context.Favorites
            .AsNoTracking()
            .OrderBy(f => f.CreatedUnix)
            .Select(f => f.ProductId)
            .ToListAsync(cancellationToken);
    }
}
