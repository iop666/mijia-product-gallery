namespace MijiaProductGallery.Core.Enums;

/// <summary>产品使用行为类型（用于计数与最近使用记录）。</summary>
public enum UsageType
{
    /// <summary>查看详情。</summary>
    View,

    /// <summary>复制图片。</summary>
    Copy,

    /// <summary>拖拽图片到外部目标。</summary>
    Drag,

    /// <summary>复制文本（名称/型号/品牌/完整信息）。</summary>
    CopyText,
}
