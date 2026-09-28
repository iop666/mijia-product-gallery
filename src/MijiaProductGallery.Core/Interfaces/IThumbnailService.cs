namespace MijiaProductGallery.Core.Interfaces;

/// <summary>缩略图缓存：按内容 SHA 绑定命名，可删除重建，永不触碰原图。</summary>
public interface IThumbnailService
{
    /// <summary>
    /// 取得缩略图绝对路径：命中且完好直接返回；缺失或损坏则由原图重建。
    /// 原图缺失或不可解码时抛出异常，不生成占位文件。
    /// </summary>
    Task<string> EnsureThumbnailAsync(string imageFileName, string sha256, CancellationToken cancellationToken = default);

    /// <summary>删除指定图的缩略图（原图更换后旧缩略图由调用方清理）。</summary>
    Task DeleteThumbnailAsync(string imageFileName, string sha256, CancellationToken cancellationToken = default);

    /// <summary>清空缩略图目录（全量重建的第一步）。</summary>
    Task DeleteAllThumbnailsAsync(CancellationToken cancellationToken = default);
}
