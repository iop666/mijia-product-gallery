namespace MijiaProductGallery.Core.Models;

/// <summary>
/// 米家百科 byCategory 接口产品对象（productSimpleVoList 元素）的字段子集。
/// 注意：PtId 是产品自身属性，与分类 ptId 不是同一含义，归类判定不得使用该字段。
/// </summary>
public sealed record BaikeProductDto
{
    /// <summary>接口字段 model。</summary>
    public required string Model { get; init; }

    /// <summary>接口字段 name。</summary>
    public required string Name { get; init; }

    /// <summary>接口字段 brand。</summary>
    public required string Brand { get; init; }

    /// <summary>接口字段 realIcon（可能含 HTML 实体，使用前需经 UrlRules 规范化）。</summary>
    public required string RealIcon { get; init; }

    /// <summary>接口字段 createTime（Unix 秒）。</summary>
    public long CreateTimeUnix { get; init; }

    /// <summary>接口字段 updateTime（Unix 秒）。</summary>
    public long UpdateTimeUnix { get; init; }

    /// <summary>接口字段 ptId，仅留存备查。</summary>
    public int PtId { get; init; }
}
