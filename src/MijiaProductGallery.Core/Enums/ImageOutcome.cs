namespace MijiaProductGallery.Core.Enums;

/// <summary>图片对比结论。</summary>
public enum ImageOutcome
{
    /// <summary>图片无变化或本轮不涉及图片。</summary>
    None,

    /// <summary>官网提示可能变化，图片尚未下载复核。</summary>
    PendingReview,

    /// <summary>同一产品的图片更换：新图原地替换，旧图不留存。</summary>
    ImageChanged,

    /// <summary>不同设备复用型号：旧图改名进入 .old 链，新图占用原文件名。</summary>
    IdReused,
}
