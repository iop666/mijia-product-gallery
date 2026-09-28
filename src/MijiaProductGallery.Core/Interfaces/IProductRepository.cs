using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>产品官方数据仓储。官方列更新仅允许经 UpdateOfficialFieldsAsync 的列白名单。</summary>
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Product?> GetByModelAsync(string model, CancellationToken cancellationToken = default);

    Task<bool> ExistsByModelAsync(string model, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>全部产品（同步引擎构建本地快照用）。</summary>
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Product product, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken = default);

    /// <summary>按官方列白名单更新产品（同步引擎唯一入口；不含 FirstSeenUnix，不触碰任何用户数据）。</summary>
    Task UpdateOfficialFieldsAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>
    /// 图片复核通过（下载 SHA 与本地一致）后采纳官网更新时间，避免种子导入的无时间戳行每轮重复复核。
    /// 仅写 UpdateTimeUnix 与 LastSeenUnix 两个白名单列。
    /// </summary>
    Task UpdateSyncTimestampsAsync(IReadOnlyList<(string Model, long UpdateTimeUnix)> items, long lastSeenUnix, CancellationToken cancellationToken = default);
}
