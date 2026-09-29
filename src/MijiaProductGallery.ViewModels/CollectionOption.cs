namespace MijiaProductGallery.ViewModels;

/// <summary>收藏夹切换选项（Id=0 表示默认的星标收藏）。</summary>
public sealed record CollectionOption(int Id, string Name)
{
    /// <summary>收藏视图默认项：星标收藏（Favorites 表语义）。</summary>
    public static CollectionOption Default { get; } = new(0, "默认收藏（星标）");

    public bool IsDefault => Id == 0;
}
