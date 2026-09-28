namespace MijiaProductGallery.Core.Models;

/// <summary>Seed 导入阶段。</summary>
public enum SeedImportStage
{
    /// <summary>校验 manifest 与包结构。</summary>
    Validating,

    /// <summary>逐文件校验并复制图片。</summary>
    CopyingImages,

    /// <summary>产品数据事务入库。</summary>
    WritingDatabase,

    /// <summary>收口（快照日期、状态）。</summary>
    Finalizing,
}

/// <summary>Seed 导入进度（供未来初始化页与测试使用）。</summary>
public sealed record SeedImportProgress
{
    public required SeedImportStage Stage { get; init; }

    public int ProductsTotal { get; init; }

    public int ProductsDone { get; init; }

    public int ImagesTotal { get; init; }

    public int ImagesDone { get; init; }

    /// <summary>当前处理的文件名（官网真实名）。</summary>
    public string? CurrentFile { get; init; }
}

/// <summary>Seed 导入结果统计。</summary>
public sealed record SeedImportResult
{
    /// <summary>种子包快照日期（yyyy-MM-dd）。</summary>
    public required string SnapshotDate { get; init; }

    public required int ProductsTotal { get; init; }

    /// <summary>本轮新插入的产品行数。</summary>
    public required int ProductsAdded { get; init; }

    /// <summary>本轮按官方列更新的既有产品行数。</summary>
    public required int ProductsUpdated { get; init; }

    /// <summary>图片文件总数（含 .old 历史图与 aux）。</summary>
    public required int ImageFilesTotal { get; init; }

    /// <summary>磁盘已存在且 SHA 一致而跳过复制的图片数。</summary>
    public required int ImagesSkipped { get; init; }

    /// <summary>实际复制的图片数（首次入库或修复损坏）。</summary>
    public required int ImagesCopied { get; init; }

    /// <summary>发现磁盘现图损坏并被修复的图片数。</summary>
    public required int ImagesRepaired { get; init; }
}
