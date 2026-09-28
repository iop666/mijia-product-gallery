namespace MijiaProductGallery.Core.Models;

/// <summary>收藏条目（用户数据）。</summary>
public sealed record FavoriteItem
{
    public required int ProductId { get; init; }

    public required long CreatedUnix { get; init; }
}
