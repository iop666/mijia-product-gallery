using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;

namespace MijiaProductGallery.Infrastructure.Sync;

/// <summary>
/// 收藏服务：经 IFavoritesRepository 落盘（用户数据），全部操作幂等；
/// 每次操作独立上下文（与 UsageService 同模式，进程内写闸串行化）。
/// </summary>
public sealed class FavoriteService(
    IDbContextFactory<GalleryDbContext> contextFactory) : IFavoriteService
{
    private static readonly SemaphoreSlim WriteGate = new(1, 1);

    public async Task<bool> IsFavoriteAsync(int productId, CancellationToken cancellationToken = default)
    {
        await WriteGate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            return await new FavoritesRepository(context).IsFavoriteAsync(productId, cancellationToken);
        }
        finally
        {
            WriteGate.Release();
        }
    }

    public async Task AddAsync(int productId, CancellationToken cancellationToken = default)
    {
        await WriteGate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            await new FavoritesRepository(context).AddAsync(productId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), cancellationToken);
        }
        finally
        {
            WriteGate.Release();
        }
    }

    public async Task RemoveAsync(int productId, CancellationToken cancellationToken = default)
    {
        await WriteGate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            await new FavoritesRepository(context).RemoveAsync(productId, cancellationToken);
        }
        finally
        {
            WriteGate.Release();
        }
    }

    public async Task<bool> ToggleAsync(int productId, CancellationToken cancellationToken = default)
    {
        await WriteGate.WaitAsync(cancellationToken);
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var repository = new FavoritesRepository(context);
            if (await repository.IsFavoriteAsync(productId, cancellationToken))
            {
                await repository.RemoveAsync(productId, cancellationToken);
                return false;
            }

            await repository.AddAsync(productId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), cancellationToken);
            return true;
        }
        finally
        {
            WriteGate.Release();
        }
    }
}
