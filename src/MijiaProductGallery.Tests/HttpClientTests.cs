using System.Net;
using System.Text;
using MijiaProductGallery.Infrastructure.Http;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>米家百科接口客户端测试：翻页、UA、瞬态重试、限速与业务错误。</summary>
public sealed class BaikeApiClientTests
{
    [Fact]
    public async Task GetCategories_PagesUntilShortPage()
    {
        var handler = new StubHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            var page = int.Parse(url[^1].ToString());
            var count = page == 1 ? 20 : 3;
            return Json(HttpStatusCode.OK, CategoriesJson(count));
        });
        using var client = new BaikeApiClient(handler, requestIntervalMilliseconds: 0);

        var categories = await client.GetCategoriesAsync();

        Assert.Equal(23, categories.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("pageIndex=1", handler.Requests[0].RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Contains("pageIndex=2", handler.Requests[1].RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Requests_CarryBrowserUserAgent()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, CategoriesJson(0)));
        using var client = new BaikeApiClient(handler, requestIntervalMilliseconds: 0);

        await client.GetCategoriesAsync();

        var userAgent = handler.Requests[0].Headers.UserAgent.ToString();
        Assert.Contains("Mozilla/5.0", userAgent, StringComparison.Ordinal);
        Assert.Contains("Chrome", userAgent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transient5xx_IsRetriedThenSucceeds()
    {
        var failures = 0;
        var handler = new StubHandler(_ =>
        {
            if (Interlocked.Increment(ref failures) == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return Json(HttpStatusCode.OK, CategoriesJson(0));
        });
        using var client = new BaikeApiClient(handler, requestIntervalMilliseconds: 0);

        var categories = await client.GetCategoriesAsync();

        Assert.Empty(categories);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ApiBusinessError_ThrowsBaikeApiException()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"result":"error","code":401,"data":null}"""));
        using var client = new BaikeApiClient(handler, requestIntervalMilliseconds: 0);

        await Assert.ThrowsAsync<BaikeApiException>(() => client.GetCategoriesAsync());
    }

    [Fact]
    public async Task Requests_AreRateLimited()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, CategoriesJson(0)));
        using var client = new BaikeApiClient(handler, requestIntervalMilliseconds: 80);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 3; i++)
        {
            await client.GetCategoriesAsync();
        }

        stopwatch.Stop();
        // 3 次请求之间至少有 2 个完整限速间隔。
        Assert.True(stopwatch.ElapsedMilliseconds >= 150, $"间隔过短：{stopwatch.ElapsedMilliseconds}ms");
        Assert.Equal(3, handler.Requests.Count);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static string CategoriesJson(int count)
    {
        var items = string.Join(",", Enumerable.Range(0, count).Select(i =>
            $"{{\"ptId\":{i},\"name\":\"分类{i}\"}}"));
        return "{\"result\":\"ok\",\"code\":0,\"data\":{\"list\":[" + items + "]}}";
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }
}

/// <summary>官方图片下载器测试：重试、白名单与最终失败。</summary>
public sealed class ImageDownloaderTests
{
    [Fact]
    public async Task Transient5xx_IsRetriedThenReturnsBytes()
    {
        var failures = 0;
        var bytes = ImageFixtures.CreatePng();
        var handler = new StubHandler(_ =>
        {
            if (Interlocked.Increment(ref failures) == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
            }

            return Bytes(bytes);
        });
        using var downloader = new ImageDownloader(handler);

        var result = await downloader.DownloadAsync("https://cdn.cnbj1.fds.api.mi-img.com/x.png");

        Assert.Equal(bytes, result);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task DisallowedHost_ThrowsWithoutHttpRequest()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("不应发起请求"));
        using var downloader = new ImageDownloader(handler);

        await Assert.ThrowsAsync<ImageDownloadException>(
            () => downloader.DownloadAsync("https://evil.example.com/x.png"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Persistent5xx_FailsAfterAllRetries()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var downloader = new ImageDownloader(handler, maxRetries: 3);

        await Assert.ThrowsAsync<ImageDownloadException>(
            () => downloader.DownloadAsync("https://cdn.cnbj1.fds.api.mi-img.com/x.png"));

        Assert.Equal(4, handler.Requests.Count);
    }

    private static HttpResponseMessage Bytes(byte[] bytes)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }
}
