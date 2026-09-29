using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>
/// 收藏夹 ZIP 导出服务：只读取条目数据与原图文件，流式写入 ZIP（不整包驻留内存），
/// 不写数据库、不改原图。缺失/不可读图片计入失败明细，绝不生成空文件。
/// </summary>
public interface ICollectionExportService
{
    /// <summary>执行导出；取消时删除半成品并返回 Cancelled=true。</summary>
    Task<CollectionExportResult> ExportAsync(
        CollectionExportRequest request,
        IProgress<CollectionExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
