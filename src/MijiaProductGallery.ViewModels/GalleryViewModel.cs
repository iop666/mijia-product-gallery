using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MijiaProductGallery.Core.Interfaces;

namespace MijiaProductGallery.ViewModels;

/// <summary>图库加载状态。</summary>
public enum GalleryLoadState
{
    /// <summary>读取中。</summary>
    Loading,

    /// <summary>就绪（有产品可展示）。</summary>
    Ready,

    /// <summary>空图库（数据库无产品，等待初始化）。</summary>
    Empty,

    /// <summary>读取失败。</summary>
    Error,
}

/// <summary>
/// 图库页视图模型：一次性读取轻量产品行（约万行级纯数据），
/// 展示交给虚拟化布局，图片按视口按需经 ThumbnailLoadQueue 加载。
/// </summary>
public partial class GalleryViewModel : ObservableObject
{
    private readonly IProductRepository products;
    private readonly ThumbnailLoadQueue thumbnailQueue;

    public GalleryViewModel(IProductRepository products, ThumbnailLoadQueue thumbnailQueue)
    {
        this.products = products;
        this.thumbnailQueue = thumbnailQueue;
    }

    public ThumbnailLoadQueue Thumbnails => thumbnailQueue;

    public ObservableCollection<ProductCard> Cards { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoadingVisible), nameof(IsEmptyVisible), nameof(IsErrorVisible), nameof(IsReadyVisible))]
    private GalleryLoadState state = GalleryLoadState.Loading;

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>四种状态对应的可见性（供 x:Bind 直绑）。</summary>
    public bool IsLoadingVisible => State == GalleryLoadState.Loading;

    public bool IsEmptyVisible => State == GalleryLoadState.Empty;

    public bool IsErrorVisible => State == GalleryLoadState.Error;

    public bool IsReadyVisible => State == GalleryLoadState.Ready;

    /// <summary>加载全部产品行；可重复调用（重新载入）。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        State = GalleryLoadState.Loading;
        ErrorMessage = null;
        try
        {
            var rows = await products.GetAllAsync(cancellationToken);
            Cards.Clear();
            foreach (var row in rows)
            {
                Cards.Add(new ProductCard(row));
            }

            State = Cards.Count == 0 ? GalleryLoadState.Empty : GalleryLoadState.Ready;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            State = GalleryLoadState.Error;
            ErrorMessage = exception.Message;
        }
    }

    /// <summary>卡片进入视口：请求缩略图加载（由 ItemsRepeater 的 ElementRealized 调用）。</summary>
    public void CardRealized(ProductCard card)
    {
        thumbnailQueue.Request(card);
    }
}
