namespace MijiaProductGallery.Core.Interfaces;

/// <summary>官方图片下载契约。实现方负责重试与退避；并发上限由调用方控制（≤8）。</summary>
public interface IImageDownloader
{
    /// <summary>下载图片内容；重试耗尽或域名不在白名单时抛出异常，调用方保持旧状态。</summary>
    Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken = default);
}
