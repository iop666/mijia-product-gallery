using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Interfaces;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>收藏仓储（用户数据）。应用内经工厂为每个操作新建上下文，避免与图库查询并发冲突。</summary>
public sealed class FavoritesRepository : IFavoritesRepository
{
    private readonly GalleryDbContext? shared;
    private readonly IDbContextFactory<GalleryDbContext>? factory;

    public FavoritesRepository(GalleryDbContext context) => shared = context;

    public FavoritesRepository(IDbContextFactory<GalleryDbContext> factory) => this.factory = factory;

    private DbContextScope Rent() => new(factory, shared);

    public async Task AddAsync(int productId, long createdUnix, CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        var exists = await scope.Context.Favorites.AnyAsync(f => f.ProductId == productId, cancellationToken);
        if (exists)
        {
            return;
        }

        scope.Context.Favorites.Add(new Core.Models.FavoriteItem { ProductId = productId, CreatedUnix = createdUnix });
        await scope.Context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        await scope.Context.Favorites
            .Where(f => f.ProductId == productId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<bool> IsFavoriteAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        return await scope.Context.Favorites.AnyAsync(f => f.ProductId == productId, cancellationToken);
    }

    public async Task<bool> ToggleAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        var context = scope.Context;
        if (await context.Favorites.AnyAsync(f => f.ProductId == productId, cancellationToken))
        {
            await context.Favorites
                .Where(f => f.ProductId == productId)
                .ExecuteDeleteAsync(cancellationToken);
            return false;
        }

        context.Favorites.Add(new Core.Models.FavoriteItem
        {
            ProductId = productId,
            CreatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<int>> GetFavoriteProductIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = Rent();
        return await scope.Context.Favorites
            .AsNoTracking()
            .OrderBy(f => f.CreatedUnix)
            .Select(f => f.ProductId)
            .ToListAsync(cancellationToken);
    }
}
