using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using MijiaProductGallery.Core;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Infrastructure;
using MijiaProductGallery.Infrastructure.Database.Repositories;
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
    private readonly string initialTheme = AppSettingsKeys.ThemeDefault;

    /// <summary>启动时读取的持久化主题（System/Light/Dark/Gray），供窗口层做主题相关微调。</summary>
    public static string InitialTheme { get; private set; } = AppSettingsKeys.ThemeDefault;

    public static IServiceProvider Services { get; private set; } = null!;

    public static Window? MainWindow { get; set; }

    public App()
    {
        // 主题为应用级资源，只能在构造函数（XAML 资源加载前）设置；运行时切换经重启应用完成。
        initialTheme = ReadPersistedTheme();
        InitialTheme = initialTheme;
        if (initialTheme is "Dark" or "Gray")
        {
            // 灰色模式为深色变体（Adobe 风格中灰），应用级同样走深色主题字典。
            RequestedTheme = ApplicationTheme.Dark;
        }
        else if (initialTheme == "Light")
        {
            RequestedTheme = ApplicationTheme.Light;
        }

        InitializeComponent();
    }

    /// <summary>直接读取设置库中的持久化主题（此时尚不可走依赖注入）。</summary>
    private static string ReadPersistedTheme()
    {
        try
        {
            var dbFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MijiaProductGallery", "database", "gallery.db");
            if (!File.Exists(dbFile))
            {
                return AppSettingsKeys.ThemeDefault;
            }

            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = dbFile,
                    Pooling = false,
                }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key";
            command.Parameters.AddWithValue("$key", AppSettingsKeys.Theme);
            var value = command.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                return AppSettingsKeys.ThemeDefault;
            }

            return System.Text.Json.JsonSerializer.Deserialize<string>(value) ?? AppSettingsKeys.ThemeDefault;
        }
        catch
        {
            return AppSettingsKeys.ThemeDefault;
        }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
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
        services.AddSingleton<IFavoriteService, FavoriteService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<ISystemClipboard, Services.ClipboardService>();
        services.AddSingleton<CardActionService>();
        services.AddSingleton<StatisticsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SyncCenterViewModel>();
        Services = services.BuildServiceProvider();

        // 灰色调色板按启动主题挂载/卸载（仅灰色模式有覆盖）。
        ThemePalette.Apply(initialTheme);
        ThemeManager.CurrentTheme = initialTheme;

        // 缩略图参数：启动时从持久化设置加载（加载/更新都会写入共享 ThumbnailSettings）。
        await Services.GetRequiredService<ILibraryRuntimeOptions>().LoadAsync();

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
