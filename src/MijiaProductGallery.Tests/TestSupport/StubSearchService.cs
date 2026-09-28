using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Tests.TestSupport;

/// <summary>搜索服务桩：返回脚本化结果，记录调用。</summary>
public sealed class StubSearchService(IReadOnlyList<Product> results) : ISearchService
{
    public int CallCount { get; private set; }

    public string? LastKeyword { get; private set; }

    public Task<IReadOnlyList<Product>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastKeyword = keyword;
        return Task.FromResult(results);
    }
}
