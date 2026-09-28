using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>
/// Seed Package（seedpack-v1）导入契约。实现保证：幂等可重入、逐文件 SHA 校验、
/// 事务化入库（失败不破坏既有数据库）、只写官方列、绝不触碰用户数据。
/// </summary>
public interface ISeedImporter
{
    /// <summary>导入 seedpack-v1 包（.zip）。包校验不通过抛出 SeedPackageException。</summary>
    Task<SeedImportResult> ImportAsync(
        string packagePath,
        IProgress<SeedImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
