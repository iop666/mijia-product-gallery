using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>产品官方数据仓储。官方列更新走显式列白名单（ExecuteUpdate），不可能触碰用户数据。</summary>
public sealed class ProductRepository(GalleryDbContext context) : IProductRepository
{
    public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public Task<Product?> GetByModelAsync(string model, CancellationToken cancellationToken = default)
    {
        return context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Model == model, cancellationToken);
    }

    public Task<bool> ExistsByModelAsync(string model, CancellationToken cancellationToken = default)
    {
        return context.Products.AnyAsync(p => p.Model == model, cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return context.Products.CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await context.Products.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return await context.Products
            .AsNoTracking()
            .Select(p => p.Category)
            .Distinct()
            .OrderBy(category => category)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetBrandsAsync(CancellationToken cancellationToken = default)
    {
        return await context.Products
            .AsNoTracking()
            .Select(p => p.Brand)
            .Distinct()
            .OrderBy(brand => brand)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        context.Products.Add(product);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRangeAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken = default)
    {
        context.Products.AddRange(products);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>复核采纳官网时间戳：仅写 UpdateTimeUnix 与 LastSeenUnix 两个白名单列。</summary>
    public async Task UpdateSyncTimestampsAsync(
        IReadOnlyList<(string Model, long UpdateTimeUnix)> items,
        long lastSeenUnix,
        CancellationToken cancellationToken = default)
    {
        foreach (var (model, updateTime) in items)
        {
            await context.Products
                .Where(p => p.Model == model)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.UpdateTimeUnix, updateTime)
                    .SetProperty(p => p.LastSeenUnix, lastSeenUnix), cancellationToken);
        }
    }

    /// <summary>官方列白名单：唯一允许同步引擎写入 Products 的路径。刻意不含 FirstSeenUnix。</summary>
    public async Task UpdateOfficialFieldsAsync(Product product, CancellationToken cancellationToken = default)
    {
        await context.Products
            .Where(p => p.Id == product.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Name, product.Name)
                .SetProperty(p => p.Brand, product.Brand)
                .SetProperty(p => p.Category, product.Category)
                .SetProperty(p => p.SubCategory, product.SubCategory)
                .SetProperty(p => p.ImageFileName, product.ImageFileName)
                .SetProperty(p => p.ImagePath, product.ImagePath)
                .SetProperty(p => p.ImageUrl, product.ImageUrl)
                .SetProperty(p => p.ImageFormat, product.ImageFormat)
                .SetProperty(p => p.ImageWidth, product.ImageWidth)
                .SetProperty(p => p.ImageHeight, product.ImageHeight)
                .SetProperty(p => p.FileSize, product.FileSize)
                .SetProperty(p => p.Sha256, product.Sha256)
                .SetProperty(p => p.IsAvailable, product.IsAvailable)
                .SetProperty(p => p.CreateTimeUnix, product.CreateTimeUnix)
                .SetProperty(p => p.UpdateTimeUnix, product.UpdateTimeUnix)
                .SetProperty(p => p.LastSeenUnix, product.LastSeenUnix), cancellationToken);
    }
}
