namespace MijiaProductGallery.Core.Enums;

/// <summary>官网数据与本地数据对比产生的变更类型。</summary>
public enum ChangeType
{
    /// <summary>无数据变化（仅图片待复核等中间态）。</summary>
    None,

    /// <summary>官网新增型号。</summary>
    New,

    /// <summary>官网已移除型号；本地标记下架，数据与图片一律保留。</summary>
    Delisted,

    /// <summary>型号归属大类变化。</summary>
    CategoryChanged,

    /// <summary>产品名称或品牌变化（含空白等微调）。</summary>
    NameChanged,

    /// <summary>同一产品的官方图片更换（名称与品牌未实质变化）。</summary>
    ImageChanged,

    /// <summary>明显不同设备复用同一型号；旧图按 .old 链保留，新图占用原文件名。</summary>
    IdReused,

    /// <summary>官网重新出现此前下架的型号；本地恢复在架状态。</summary>
    Relisted,
}
