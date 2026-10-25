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
        // 历史数据根（%LOCALAPPDATA%）一次性迁移到应用目录\Data；
        // 必须先于任何数据库访问（含持久化主题读取）执行。
        MijiaProductGallery.Infrastructure.Database.DataRootMigrator.Migrate(
            new MijiaProductGallery.Infrastructure.Database.DatabasePaths().Root);

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
            var dbFile = new MijiaProductGallery.Infrastructure.Database.DatabasePaths().DatabaseFile;
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
        services.AddSingleton<Core.Interfaces.IAppDataRoot>(new MijiaProductGallery.Infrastructure.Database.DatabasePaths());
        services.AddSingleton<IUiDispatcher>(new UiDispatcher(dispatcherQueue));
        services.AddSingleton<ThumbnailLoadQueue>();
        services.AddSingleton<GalleryViewModel>();
        services.AddSingleton<InitializationViewModel>();
        services.AddSingleton<IUsageService, UsageService>();
        services.AddSingleton<IFavoriteService, FavoriteService>();
        services.AddSingleton<ISystemClipboard, Services.ClipboardService>();
        services.AddSingleton<Core.Interfaces.ICollectionExportService, CollectionExportService>();
        services.AddSingleton<CardActionService>();
        services.AddSingleton<StatisticsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SyncCenterViewModel>();
        Services = services.BuildServiceProvider();

        // 回收上次进程被强杀（关闭初始化控制台/崩溃/断电）遗留的图片复核临时目录；
        // 锁文件保证运行中的同步实例不受影响。后台执行，不拖慢启动。
        _ = Task.Run(() => SyncTempDirectory.CleanupOrphans());

        // 灰色调色板按启动主题挂载/卸载（仅灰色模式有覆盖）。
        ThemePalette.Apply(initialTheme);
        ThemeManager.CurrentTheme = initialTheme;

        // 先确保数据库结构（空库建表 / 迁移），任何依赖数据库的启动加载都必须在其后。
        await Services.GetRequiredService<MijiaProductGallery.Infrastructure.Database.DbInitializer>().InitializeAsync();

        // 缩略图参数：启动时从持久化设置加载（加载/更新都会写入共享 ThumbnailSettings）。
        await Services.GetRequiredService<ILibraryRuntimeOptions>().LoadAsync();

        var settings = Services.GetRequiredService<ISettingsRepository>();
        var hasProducts = await Services.GetRequiredService<IProductRepository>().CountAsync() > 0;
        var licenseAgreed = await settings.GetValueAsync(AppSettingsKeys.LicenseAgreed, false);
        if (!hasProducts || !licenseAgreed)
        {
            // 主窗口在首次许可/初始化完成前不创建：期间不会出现可切换的导航界面。
            startupWindow = new Views.StartupWindow(
                settings,
                Services.GetRequiredService<IFirstRunInitializer>(),
                Services.GetRequiredService<ISyncService>(),
                Services.GetRequiredService<IAppDataRoot>(),
                Services.GetRequiredService<InitializationViewModel>(),
                needsInitialization: !hasProducts);
            startupWindow.Ready += OnStartupReady;
            startupWindow.Activate();
            _ = startupWindow.StartAsync();
            return;
        }

        OpenMainWindow();
    }

    private Views.StartupWindow? startupWindow;

    private void OnStartupReady()
    {
        startupWindow = null;
        OpenMainWindow();
    }

    private void OpenMainWindow()
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    /// <summary>全局未处理异常落盘，避免静默失败。</summary>
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var logs = new MijiaProductGallery.Infrastructure.Database.DatabasePaths().LogsDirectory;
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
