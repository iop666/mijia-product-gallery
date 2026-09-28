namespace MijiaProductGallery.Core.Models;

/// <summary>合集成员条目（用户数据）。</summary>
public sealed class CollectionItem
{
    public required int CollectionId { get; set; }

    public required int ProductId { get; set; }

    public long AddedUnix { get; set; }

    public int SortOrder { get; set; }
}
