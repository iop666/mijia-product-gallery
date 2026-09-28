using CommunityToolkit.Mvvm.ComponentModel;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.ViewModels;

/// <summary>图库卡片缩略图状态。</summary>
public enum CardThumbnailState
{
    /// <summary>等待加载。</summary>
    Pending,

    /// <summary>生成/读取中。</summary>
    Loading,

    /// <summary>已就绪（ThumbnailPath 可用）。</summary>
    Loaded,

    /// <summary>加载失败（原图缺失或不可解码）。</summary>
    Failed,
}

/// <summary>图库卡片视图模型：展示官方字段与缩略图状态。</summary>
public partial class ProductCard : ObservableObject
{
    public ProductCard(Product product)
    {
        ProductId = product.Id;
        Model = product.Model;
        Name = product.Name;
        Brand = product.Brand;
        Category = product.Category;
        ImageFileName = product.ImageFileName;
        Sha256 = product.Sha256;
        ImagePath = product.ImagePath;
        HasImage = product.Sha256 is not null && product.ImagePath is not null;
        thumbnailState = HasImage ? CardThumbnailState.Pending : CardThumbnailState.Failed;
    }

    public int ProductId { get; }

    public string Model { get; }

    public string Name { get; }

    public string Brand { get; }

    public string Category { get; }

    /// <summary>官网真实图片文件名；无图型号为 null。</summary>
    public string? ImageFileName { get; }

    public string? Sha256 { get; }

    /// <summary>相对路径（供"打开文件位置"等后续功能使用）。</summary>
    public string? ImagePath { get; }

    /// <summary>是否有可加载的图片。</summary>
    public bool HasImage { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowImage), nameof(ShowLoading), nameof(ShowFailed))]
    private CardThumbnailState thumbnailState;

    [ObservableProperty]
    private string? thumbnailPath;

    /// <summary>该卡片是否已请求过加载（避免重复入队）。</summary>
    internal bool LoadRequested { get; set; }

    internal string? LoadKey => ImageFileName is null || Sha256 is null ? null : ImageFileName + "|" + Sha256;

    /// <summary>三种缩略图状态对应的可见性（供 x:Bind 直绑）。</summary>
    public bool ShowImage => ThumbnailState == CardThumbnailState.Loaded;

    public bool ShowLoading => ThumbnailState is CardThumbnailState.Loading or CardThumbnailState.Pending;

    public bool ShowFailed => ThumbnailState == CardThumbnailState.Failed;

    /// <summary>失败占位文案：区分"无图型号"与"图片加载失败"。</summary>
    public string FailureText => HasImage ? "图片加载失败" : "无图型号";

    internal void SetLoaded(string path)
    {
        ThumbnailPath = path;
        ThumbnailState = CardThumbnailState.Loaded;
    }

    internal void SetFailed()
    {
        ThumbnailState = CardThumbnailState.Failed;
    }

    internal void SetLoading()
    {
        ThumbnailState = CardThumbnailState.Loading;
    }
}
