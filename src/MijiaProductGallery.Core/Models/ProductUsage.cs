namespace MijiaProductGallery.Core.Models;

/// <summary>产品的用户使用计数。与官方数据分表存储，同步流程不得写入。</summary>
public sealed class ProductUsage
{
    public int ProductId { get; set; }

    public int CopyCount { get; set; }

    public int DragCount { get; set; }

    public int ViewCount { get; set; }

    /// <summary>全部使用行为合计（含查看）。</summary>
    public int TotalUseCount { get; set; }

    /// <summary>最近一次使用时间（Unix 秒）；从未使用为 null。</summary>
    public long? LastUsedUnix { get; set; }
}
