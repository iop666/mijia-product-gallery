using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>
/// 原图文件存储：临时文件 → 文件头判定 → 完整解码 → SHA256 → 原子落位。
/// 实现保证：验证失败不动既有文件；任何写入都不直接覆盖原图。
/// </summary>
public interface IImageStore
{
    /// <summary>首次入库（新增型号 / 无图补图）。文件名扩展名由内容判定。</summary>
    Task<ImageStoreResult> StoreNewAsync(string model, Stream content, CancellationToken cancellationToken = default);

    /// <summary>同产品官方换图：SHA 相同返回 Unchanged，不同则新图原地替换原文件名（旧图不留存）。</summary>
    Task<ImageStoreResult> ReplaceAsync(string model, string currentImageFileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>ID 复用：SHA 不同时旧图进 .old 链链尾，新图占用原文件名。</summary>
    Task<ImageStoreResult> ReplaceWithHistoryAsync(string model, string currentImageFileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>把数据库中的相对 ImagePath 解析为绝对路径；空值或越界（防路径穿越）返回 null。</summary>
    string? ResolveAbsolutePath(string? imagePath);
}
