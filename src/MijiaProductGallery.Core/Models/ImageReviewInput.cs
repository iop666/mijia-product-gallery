namespace MijiaProductGallery.Core.Models;

/// <summary>单张图片的复核材料：由同步流程下载图片后填入，供对比规则判定图片结论。</summary>
public sealed record ImageReviewInput
{
    /// <summary>本次按官网 URL 下载得到的图片 SHA-256；尚未下载为 null（结论为待复核）。</summary>
    public string? DownloadedSha256 { get; init; }

    /// <summary>该型号磁盘上已有的 .old 系列旧图文件名，用于计算链式改名目标。</summary>
    public IReadOnlyList<string> ExistingOldImageFiles { get; init; } = [];
}
