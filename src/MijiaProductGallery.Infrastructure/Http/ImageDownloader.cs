using System.Net;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Rules;

namespace MijiaProductGallery.Infrastructure.Http;

/// <summary>官方图片下载失败（重试耗尽 / 域名白名单外 / 超限）。</summary>
public sealed class ImageDownloadException(string message) : InvalidOperationException(message);

/// <summary>
/// 官方图片下载器：仅允许官方域（home.mi.com / *.mi-img.com），瞬态失败指数退避重试；
/// 并发上限由调用方信号量控制（≤8）。
/// </summary>
public sealed class ImageDownloader : IImageDownloader, IDisposable
{
    private readonly HttpClient httpClient;
    private readonly int maxRetries;

    public ImageDownloader(int timeoutSeconds = 30, int maxRetries = 3)
        : this(new HttpClientHandler(), timeoutSeconds, maxRetries)
    {
    }

    /// <summary>以自定义 HttpMessageHandler 构造（测试注入桩响应用）。</summary>
    public ImageDownloader(HttpMessageHandler handler, int timeoutSeconds = 30, int maxRetries = 3)
    {
        this.maxRetries = maxRetries;
        httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(BaikeApiClient.DefaultUserAgent);
    }

    public async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!UrlRules.IsAllowedHost(url))
        {
            throw new ImageDownloadException($"图片地址不在官方域白名单内：{url}");
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await httpClient.GetAsync(url, cancellationToken);
                if (IsTransient(response.StatusCode) && attempt <= maxRetries)
                {
                    await BackoffAsync(attempt, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new ImageDownloadException($"图片下载 HTTP {(int)response.StatusCode}：{url}");
                }

                var length = response.Content.Headers.ContentLength;
                if (length is > ImageHeaderRules.MaxImageLength)
                {
                    throw new ImageDownloadException($"图片超过大小上限：{url}");
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length == 0)
                {
                    throw new ImageDownloadException($"图片内容为空：{url}");
                }

                return bytes;
            }
            catch (HttpRequestException) when (attempt <= maxRetries)
            {
                await BackoffAsync(attempt, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt <= maxRetries)
            {
                await BackoffAsync(attempt, cancellationToken);
            }
        }
    }

    private static async Task BackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        await Task.Delay(500 * (1 << (attempt - 1)), cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests
            || statusCode == HttpStatusCode.InternalServerError
            || statusCode == HttpStatusCode.BadGateway
            || statusCode == HttpStatusCode.ServiceUnavailable
            || statusCode == HttpStatusCode.GatewayTimeout;
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
