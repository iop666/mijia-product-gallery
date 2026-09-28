using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>
/// 米家百科接口客户端契约。实现方负责浏览器 UA、限速、超时与指数退避重试；
/// 归类判定不由客户端完成（分类归属以 byCategory 列表成员关系为准）。
/// </summary>
public interface IBaikeApiClient
{
    /// <summary>翻页取全部分类（直到返回不足一页）。</summary>
    Task<IReadOnlyList<BaikeCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>取单个分类下的全部在架产品（data.productSimpleVoList）。</summary>
    Task<IReadOnlyList<BaikeProductDto>> GetProductsByCategoryAsync(int ptId, CancellationToken cancellationToken = default);
}
