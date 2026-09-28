using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Infrastructure.Http;

/// <summary>米家百科接口异常（业务码非成功或响应结构异常）。</summary>
public sealed class BaikeApiException(string message) : InvalidOperationException(message);

/// <summary>
/// 米家百科接口客户端：浏览器 UA、请求间限速（默认 400ms）、超时与指数退避重试（默认 3 次）。
/// 所有请求串行化以满足限速约束。
/// </summary>
public sealed class BaikeApiClient : IBaikeApiClient, IDisposable
{
    public const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    private const string CategoriesEndpoint = "/cgi-op/api/v1/baike/productCategories/V1?pageSize=20&pageIndex={0}";
    private const string ProductsEndpoint = "/cgi-op/api/v1/baike/products/byCategory/V1?ptId={0}";
    private const int CategoriesPageSize = 20;

    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly int intervalMilliseconds;
    private long nextEarliestRequestTick;

    public BaikeApiClient(int requestIntervalMilliseconds = 400, int timeoutSeconds = 30, int maxRetries = 3)
        : this(new HttpClientHandler(), requestIntervalMilliseconds, timeoutSeconds, maxRetries)
    {
    }

    /// <summary>以自定义 HttpMessageHandler 构造（测试注入桩响应用）。</summary>
    public BaikeApiClient(HttpMessageHandler handler, int requestIntervalMilliseconds = 400, int timeoutSeconds = 30, int maxRetries = 3)
    {
        intervalMilliseconds = requestIntervalMilliseconds;
        MaxRetries = maxRetries;
        httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        httpClient.BaseAddress = new Uri("https://home.mi.com");
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
    }

    public int MaxRetries { get; }

    public async Task<IReadOnlyList<BaikeCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var categories = new List<BaikeCategory>();
        for (var pageIndex = 1; ; pageIndex++)
        {
            using var document = await SendForJsonAsync(string.Format(CategoriesEndpoint, pageIndex), cancellationToken);
            var list = document.RootElement.GetProperty("data").GetProperty("list");
            var pageCapacity = 0;
            foreach (var element in list.EnumerateArray())
            {
                pageCapacity++;
                categories.Add(new BaikeCategory
                {
                    PtId = element.GetProperty("ptId").GetInt32(),
                    Name = element.GetProperty("name").GetString() ?? string.Empty,
                });
            }

            if (pageCapacity < CategoriesPageSize)
            {
                return categories;
            }
        }
    }

    public async Task<IReadOnlyList<BaikeProductDto>> GetProductsByCategoryAsync(int ptId, CancellationToken cancellationToken = default)
    {
        using var document = await SendForJsonAsync(string.Format(ProductsEndpoint, ptId), cancellationToken);
        var products = new List<BaikeProductDto>();
        var list = document.RootElement.GetProperty("data").GetProperty("productSimpleVoList");
        foreach (var element in list.EnumerateArray())
        {
            products.Add(new BaikeProductDto
            {
                Model = element.GetProperty("model").GetString() ?? string.Empty,
                Name = element.GetProperty("name").GetString() ?? string.Empty,
                Brand = element.GetProperty("brand").GetString() ?? string.Empty,
                RealIcon = element.GetProperty("realIcon").GetString() ?? string.Empty,
                CreateTimeUnix = element.TryGetProperty("createTime", out var createTime) && createTime.ValueKind == JsonValueKind.Number
                    ? createTime.GetInt64()
                    : 0,
                UpdateTimeUnix = element.TryGetProperty("updateTime", out var updateTime) && updateTime.ValueKind == JsonValueKind.Number
                    ? updateTime.GetInt64()
                    : 0,
                PtId = element.TryGetProperty("ptId", out var ptIdElement) && ptIdElement.ValueKind == JsonValueKind.Number
                    ? ptIdElement.GetInt32()
                    : 0,
            });
        }

        return products;
    }

    private async Task<JsonDocument> SendForJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await AcquireRequestSlotAsync(cancellationToken);
            try
            {
                using var response = await httpClient.GetAsync(relativeUrl, cancellationToken);
                if (IsTransient(response.StatusCode) && attempt <= MaxRetries)
                {
                    await BackoffAsync(attempt, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new BaikeApiException($"米家百科接口 HTTP {(int)response.StatusCode}：{relativeUrl}");
                }

                var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
                if (document is null)
                {
                    throw new BaikeApiException($"米家百科接口返回空响应：{relativeUrl}");
                }

                var root = document.RootElement;
                var ok = root.TryGetProperty("result", out var result)
                    && string.Equals(result.GetString(), "ok", StringComparison.OrdinalIgnoreCase)
                    && (!root.TryGetProperty("code", out var code) || code.GetInt32() == 0);
                if (!ok)
                {
                    throw new BaikeApiException($"米家百科接口业务失败：{relativeUrl}");
                }

                return document;
            }
            catch (HttpRequestException) when (attempt <= MaxRetries)
            {
                await BackoffAsync(attempt, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt <= MaxRetries)
            {
                // 超时（非用户取消）：指数退避后重试；退避期间用户取消会以 OperationCanceledException 透出。
                await BackoffAsync(attempt, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    private async Task AcquireRequestSlotAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        var wait = Interlocked.Read(ref nextEarliestRequestTick) - Environment.TickCount64;
        if (wait > 0)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(wait), cancellationToken);
            }
            catch
            {
                gate.Release();
                throw;
            }
        }

        Interlocked.Exchange(ref nextEarliestRequestTick, Environment.TickCount64 + intervalMilliseconds);
    }

    private async Task BackoffAsync(int attempt, CancellationToken cancellationToken)
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
        gate.Dispose();
    }
}
