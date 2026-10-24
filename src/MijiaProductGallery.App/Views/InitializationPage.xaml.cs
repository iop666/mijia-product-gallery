using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using MijiaProductGallery.ViewModels;

namespace MijiaProductGallery.App.Views;

/// <summary>首次初始化页：状态展示（进度/阶段/错误）与开始初始化、选择种子包入口。</summary>
public sealed partial class InitializationPage : Page
{
    private readonly InitializationViewModel vm;

    public InitializationViewModel Vm => vm;

    /// <summary>初始化成功（UI 侧跳转图库）。</summary>
    public event Action? InitializationSucceeded;

    public InitializationPage(InitializationViewModel viewModel)
    {
        InitializeComponent();
        vm = viewModel;
        vm.Succeeded += () => InitializationSucceeded?.Invoke();
    }

    private async void OnRetryClick(object sender, RoutedEventArgs e)
    {
        await vm.RunAsync();
    }

    private async void OnPickSeedClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Desktop,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".zip");
        if (App.MainWindow is not null)
        {
            var handle = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, handle);
        }

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await vm.RetryWithSeedAsync(file.Path);
        }
    }
}
