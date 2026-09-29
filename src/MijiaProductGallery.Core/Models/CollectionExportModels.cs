namespace MijiaProductGallery.Core.Models;

/// <summary>收藏夹导出的单个产品条目（只读数据，ImagePath 为原图相对路径）。</summary>
public sealed record CollectionExportItem
{
    public required int ProductId { get; init; }

    public required string Model { get; init; }

    public required string Name { get; init; }

    /// <summary>原图相对路径；null 表示无图（不可导出）。</summary>
    public required string? ImagePath { get; init; }
}

/// <summary>导出请求。</summary>
public sealed record CollectionExportRequest
{
    public required IReadOnlyList<CollectionExportItem> Items { get; init; }

    /// <summary>目标 ZIP 完整路径。</summary>
    public required string ZipFilePath { get; init; }

    /// <summary>true=以 Model 命名；false=以设备名称命名（自动安全去重）。</summary>
    public required bool NameByModel { get; init; }
}

/// <summary>导出进度（已完成 / 总数）。</summary>
public sealed record CollectionExportProgress(int Completed, int Total);

/// <summary>导出结果：Cancelled=true 时 ZIP 已删除（不残留半成品）。</summary>
public sealed record CollectionExportResult
{
    public required bool Cancelled { get; init; }

    public required int ExportedCount { get; init; }

    /// <summary>失败明细（产品名（型号）：原因），导出为空文件一律计入失败。</summary>
    public required IReadOnlyList<string> FailedItems { get; init; }
}
