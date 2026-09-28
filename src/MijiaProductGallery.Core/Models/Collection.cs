namespace MijiaProductGallery.Core.Models;

/// <summary>收藏合集（用户数据）。一个产品可加入多个合集。</summary>
public sealed class Collection
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public long CreatedUnix { get; set; }

    public long UpdatedUnix { get; set; }

    public int SortOrder { get; set; }
}
