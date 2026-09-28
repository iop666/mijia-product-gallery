using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MijiaProductGallery.ViewModels;

namespace MijiaProductGallery.App.Views;

/// <summary>
/// 图库页：ItemsRepeater + UniformGridLayout 虚拟化网格；
/// 图片按视口按需经 ThumbnailLoadQueue 异步加载，UI 线程不做任何同步 IO。
/// </summary>
public sealed partial class GalleryPage : Page
{
    private readonly GalleryViewModel vm;

    public GalleryViewModel Vm => vm;

    public GalleryPage(GalleryViewModel viewModel)
    {
        InitializeComponent();
        vm = viewModel;
        DataContext = vm;
        Loaded += OnLoaded;
        CardsRepeater.ElementPrepared += OnElementPrepared;
    }

    /// <summary>导航回图库时刷新数据（首次导入完成等场景）。</summary>
    public void ActivateView()
    {
        if (vm.State is GalleryLoadState.Empty or GalleryLoadState.Error)
        {
            _ = vm.LoadAsync();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (vm.State == GalleryLoadState.Loading && vm.Cards.Count == 0)
        {
            await vm.LoadAsync();
        }
    }

    private void OnElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is ContentPresenter { Content: ProductCard card })
        {
            vm.CardRealized(card);
        }
    }

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        _ = vm.LoadAsync();
    }
}
