using MijiaProductGallery.Infrastructure.Http;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>
/// 真实接口冒烟入口：默认直接返回（等效跳过）。
/// 手动执行：dotnet test -e MIJIA_LIVE=1 --filter FullyQualifiedName~LiveApiSmoke
/// </summary>
public sealed class LiveApiSmokeTests
{
    [Fact]
    public async Task LiveCategories_AndCategoryProducts_AreReadable()
    {
        if (Environment.GetEnvironmentVariable("MIJIA_LIVE") != "1")
        {
            return;
        }

        using var client = new BaikeApiClient(requestIntervalMilliseconds: 400);
        var categories = await client.GetCategoriesAsync();
        Assert.NotEmpty(categories);
        Assert.Contains(categories, category => category.PtId == -10000);

        var target = categories.First(category => category.PtId != -10000);
        var products = await client.GetProductsByCategoryAsync(target.PtId);
        Assert.NotEmpty(products);
        Assert.All(products, product => Assert.False(string.IsNullOrWhiteSpace(product.Model)));
        Assert.All(products, product => Assert.StartsWith("http", product.RealIcon, StringComparison.Ordinal));
    }
}
