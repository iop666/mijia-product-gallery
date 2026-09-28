using System.Text.Json;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Tests.TestSupport;

/// <summary>从录制的接口夹具加载产品对象（真实响应字段子集）。</summary>
public static class ApiFixture
{
    /// <summary>路由网关（ptId=16）分类的抽样产品记录。</summary>
    public static IReadOnlyList<BaikeProductDto> LoadPtId16Sample()
    {
        using var stream = File.OpenRead(
            Path.Combine(TestPaths.FixturesRoot, "api", "byCategory-ptId16-sample.json"));
        using var document = JsonDocument.Parse(stream);

        var result = new List<BaikeProductDto>();
        foreach (var element in document.RootElement.GetProperty("productSimpleVoList").EnumerateArray())
        {
            result.Add(new BaikeProductDto
            {
                Model = element.GetProperty("model").GetString()!,
                Name = element.GetProperty("name").GetString()!,
                Brand = element.GetProperty("brand").GetString()!,
                RealIcon = element.GetProperty("realIcon").GetString()!,
                CreateTimeUnix = element.GetProperty("createTime").GetInt64(),
                UpdateTimeUnix = element.GetProperty("updateTime").GetInt64(),
                PtId = element.GetProperty("ptId").GetInt32(),
            });
        }

        return result;
    }
}
