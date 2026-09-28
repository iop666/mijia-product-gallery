using MijiaProductGallery.Core.Enums;

namespace MijiaProductGallery.Core.Models;

/// <summary>图片落盘结果状态。</summary>
public enum ImageStoreStatus
{
    /// <summary>新图首次落盘。</summary>
    StoredNew,

    /// <summary>内容与磁盘现图完全一致，未做任何写入。</summary>
    Unchanged,

    /// <summary>同产品换图：新图已替换原文件名，旧图不留存。</summary>
    Replaced,

    /// <summary>ID 复用：旧图已进入 .old 链，新图占用原文件名。</summary>
    ReplacedWithHistory,
}

/// <summary>图片写入结果：同步引擎据其更新数据库官方列。</summary>
public sealed record ImageStoreResult
{
    public required ImageStoreStatus Status { get; init; }

    /// <summary>官网真实文件名（含真实扩展名与真实 aux. 前缀）。</summary>
    public required string ImageFileName { get; init; }

    /// <summary>相对数据根的磁盘路径（安全名，`/` 分隔）。</summary>
    public required string ImagePath { get; init; }

    public required ImageFormat Format { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required long FileSize { get; init; }

    /// <summary>内容 SHA-256（十六进制小写）。</summary>
    public required string Sha256 { get; init; }

    /// <summary>ReplacedWithHistory 时旧图的去向文件名（真实名 .old 链）。</summary>
    public string? ReplacedByOldFileName { get; init; }
}
