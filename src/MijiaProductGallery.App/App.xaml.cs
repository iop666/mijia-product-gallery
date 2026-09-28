using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Infrastructure;
using MijiaProductGallery.Infrastructure.Sync;
using MijiaProductGallery.ViewModels;

namespace MijiaProductGallery.App;

/// <summary>UI 线程调度器：经 DispatcherQueue 把回调投递回 UI 线程。</summary>
public sealed class UiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue queue;

    public UiDispatcher(DispatcherQueue queue)
    {
        this.queue = queue;
    }

    public void Post(Action action)
    {
        queue.TryEnqueue(() => action());
    }
}

/// <summary>应用入口：组装依赖注入容器并启动主窗口。</summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        UnhandledException += OnUnhandledException;
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IUiDispatcher>(new UiDispatcher(dispatcherQueue));
        services.AddSingleton<ThumbnailLoadQueue>();
        services.AddSingleton<GalleryViewModel>();
        services.AddSingleton<InitializationViewModel>();
        services.AddSingleton<IUsageService, UsageService>();
        services.AddSingleton<ISystemClipboard, Services.ClipboardService>();
        services.AddSingleton<CardActionService>();
        Services = services.BuildServiceProvider();

        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    /// <summary>全局未处理异常落盘，避免静默失败。</summary>
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var logs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MijiaProductGallery", "logs");
            Directory.CreateDirectory(logs);
            File.AppendAllText(
                Path.Combine(logs, "unhandled.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {e.Message}{Environment.NewLine}{e.Exception}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败不二次抛出。
        }
    }
}
