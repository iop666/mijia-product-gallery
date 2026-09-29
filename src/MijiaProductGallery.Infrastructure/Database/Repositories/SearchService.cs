using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database;

namespace MijiaProductGallery.Infrastructure.Database.Repositories;

/// <summary>
/// 搜索服务（仅关键字维度）：组合逻辑由 FilterService 的查询管线提供，
/// 过滤与排序全部在数据库侧执行；Description/Alias 为预留字段（当前无对应列）。
/// </summary>
public sealed class SearchService(GalleryDbContext dbContext) : ISearchService
{
    public async Task<IReadOnlyList<Product>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return [];
        }

        var query = FilterService.ApplyKeyword(dbContext.Products.AsNoTracking(), keyword);
        return await FilterService.OrderBySearchRank(query, keyword).ToListAsync(cancellationToken);
    }
}
