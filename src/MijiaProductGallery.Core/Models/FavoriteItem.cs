namespace MijiaProductGallery.Core.Models;

/// <summary>收藏条目（用户数据）。</summary>
public sealed record FavoriteItem
{
    public required int ProductId { get; init; }

    public required long CreatedUnix { get; init; }

    /// <summary>收藏备注（预留字段，当前版本无 UI；默认 null）。</summary>
    public string? Note { get; set; }
}
