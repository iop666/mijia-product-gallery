using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MijiaProductGallery.App.Services;
using MijiaProductGallery.Core;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.ViewModels;
using Windows.Graphics;

namespace MijiaProductGallery.App.Views;

/// <summary>
/// 首次启动与未完成初始化的独立窗口：先展示许可协议，再负责初始化。
/// 初始化本身不支持断点续传，关闭加载窗口或失败后重新启动会从头重来；
/// 主窗口在此期间不创建，避免用户切换导航页打断初始化。
/// </summary>
public sealed partial class StartupWindow : Window
{
    private readonly ISettingsRepository settings;
    private readonly IFirstRunInitializer initializer;
    private readonly ISyncService syncService;
    private readonly IAppDataRoot dataRoot;
    private readonly InitializationViewModel vm;
    private readonly bool needsInitialization;
    private InitializationPage? initializationPage;
    private bool consoleRunning;
    private bool completed;

    /// <summary>初始化完成（或仅需同意许可）后触发：宿主创建并显示主窗口。</summary>
    public event Action? Ready;

    public StartupWindow(
        ISettingsRepository settings,
        IFirstRunInitializer initializer,
        ISyncService syncService,
        IAppDataRoot dataRoot,
        InitializationViewModel vm,
        bool needsInitialization)
    {
        this.settings = settings;
        this.initializer = initializer;
        this.syncService = syncService;
        this.dataRoot = dataRoot;
        this.vm = vm;
        this.needsInitialization = needsInitialization;

        InitializeComponent();
        Title = needsInitialization ? "米家产品示例图库 · 初始化" : "米家产品示例图库";
        RootGrid.RequestedTheme = ThemeManager.ToElementTheme(ThemeManager.CurrentTheme);
        // 必须等 XamlRoot 就绪：在 Activated 阶段它可能仍为 null，按 1.0 兜底会把
        // 820×700 当成物理像素，在高 DPI 下窗口过小并裁掉内容。
        RootGrid.Loaded += OnRootLoaded;
    }

    /// <summary>窗口加载后按实际 DPI 调整尺寸并居中（AppWindow.Resize 接收物理像素）。</summary>
    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= OnRootLoaded;
        var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
        var width = (int)(820 * scale);
        var height = (int)(700 * scale);
        AppWindow.Resize(new SizeInt32(width, height));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(
            area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2));
    }

    /// <summary>启动时决定先展示许可，还是直接进入初始化。</summary>
    public async Task StartAsync()
    {
        var agreed = await settings.GetValueAsync(AppSettingsKeys.LicenseAgreed, false);
        if (!agreed)
        {
            ShowLicense();
            return;
        }

        await BeginAsync();
    }

    private void ShowLicense()
    {
        LicensePanel.Visibility = Visibility.Visible;
        InitPanel.Visibility = Visibility.Collapsed;
    }

    private async void OnAgreeClick(object sender, RoutedEventArgs e)
    {
        AgreeButton.IsEnabled = false;
        LicenseError.Visibility = Visibility.Collapsed;
        try
        {
            await settings.SetValueAsync(AppSettingsKeys.LicenseAgreed, true);
            await BeginAsync();
        }
        catch (Exception exception)
        {
            LicenseError.Text = $"无法保存许可状态：{exception.Message}";
            LicenseError.Visibility = Visibility.Visible;
        }
        finally
        {
            AgreeButton.IsEnabled = true;
        }
    }

    private void OnDeclineClick(object sender, RoutedEventArgs e)
    {
        Application.Current.Exit();
    }

    private async Task BeginAsync()
    {
        if (!needsInitialization)
        {
            Complete();
            return;
        }

        // 初始化不支持断点续传：上次被打断（关闭窗口/失败）后重新启动，直接从头开始。
        await RunConsoleAsync();
    }

    private void EnsureInitializationPage()
    {
        if (initializationPage is not null)
        {
            return;
        }

        initializationPage = new InitializationPage(vm, this);
        initializationPage.InitializationSucceeded += Complete;
        initializationPage.PrimaryActionRequested += RunConsoleAsync;
        InitHost.Content = initializationPage;
    }

    /// <summary>在线初始化走控制台加载窗口（含下载目录、阶段与剩余时间）；窗口隐藏期间不可切换页面。</summary>
    private async Task RunConsoleAsync()
    {
        if (consoleRunning)
        {
            return;
        }

        consoleRunning = true;
        try
        {
            AppWindow.Hide();
            var result = await FirstRunConsole.RunAsync(initializer, syncService, dataRoot);
            if (result == FirstRunConsoleResult.Succeeded)
            {
                Complete();
                return;
            }

            EnsureInitializationPage();
            LicensePanel.Visibility = Visibility.Collapsed;
            InitPanel.Visibility = Visibility.Visible;
            vm.MarkFailed(result == FirstRunConsoleResult.ConsoleUnavailable
                ? "无法打开加载窗口。请重试或选择本地种子包。"
                : "初始化未完成。请重试或选择本地种子包。");
            AppWindow.Show();
            Activate();
        }
        finally
        {
            consoleRunning = false;
        }
    }

    private void Complete()
    {
        if (completed)
        {
            return;
        }

        completed = true;
        Ready?.Invoke();
        Close();
    }
}
