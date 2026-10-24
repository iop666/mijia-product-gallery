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
    private readonly Window hostWindow;

    public InitializationViewModel Vm => vm;

    /// <summary>初始化成功（UI 侧切到主界面）。</summary>
    public event Action? InitializationSucceeded;

    /// <summary>主按钮请求：由宿主决定用控制台还是页内进度执行初始化。</summary>
    public event Func<Task>? PrimaryActionRequested;

    public InitializationPage(InitializationViewModel viewModel, Window hostWindow)
    {
        InitializeComponent();
        vm = viewModel;
        this.hostWindow = hostWindow;
        vm.Succeeded += () => InitializationSucceeded?.Invoke();
    }

    private async void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (PrimaryActionRequested is { } handler)
        {
            await handler();
            return;
        }

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
        var handle = WindowNative.GetWindowHandle(hostWindow);
        InitializeWithWindow.Initialize(picker, handle);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await vm.RetryWithSeedAsync(file.Path);
        }
    }
}
