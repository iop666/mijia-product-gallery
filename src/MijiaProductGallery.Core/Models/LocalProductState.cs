namespace MijiaProductGallery.Core.Models;

/// <summary>本地产品快照：对比规则的本地侧输入（仅官方字段）。</summary>
public sealed record LocalProductState
{
    public required string Model { get; init; }

    public required string Name { get; init; }

    public required string Brand { get; init; }

    public required string Category { get; init; }

    /// <summary>现用图片文件名；无图型号为 null。</summary>
    public string? ImageFileName { get; init; }

    /// <summary>现用图片内容 SHA-256；无图或未计算为 null。</summary>
    public string? ImageSha256 { get; init; }

    public bool IsAvailable { get; init; } = true;

    public long? CreateTimeUnix { get; init; }

    public long? UpdateTimeUnix { get; init; }

    /// <summary>由本地存储的官方产品记录构造对比输入。</summary>
    public static LocalProductState FromProduct(Product product) => new()
    {
        Model = product.Model,
        Name = product.Name,
        Brand = product.Brand,
        Category = product.Category,
        ImageFileName = product.ImageFileName,
        ImageSha256 = product.Sha256,
        IsAvailable = product.IsAvailable,
        CreateTimeUnix = product.CreateTimeUnix,
        UpdateTimeUnix = product.UpdateTimeUnix,
    };
}
