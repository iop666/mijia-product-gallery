using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MijiaProductGallery.Core;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.App.Services;

/// <summary>首次启动控制台流程结果。</summary>
public enum FirstRunConsoleResult
{
    /// <summary>初始化成功（数据库已有产品）。</summary>
    Succeeded,

    /// <summary>初始化失败（宿主应回退到初始化页提供重试/选种子包）。</summary>
    Failed,

    /// <summary>无法分配控制台（宿主应回退到 GUI 初始化页）。</summary>
    ConsoleUnavailable,
}

/// <summary>
/// 首次启动加载窗口：分配真实控制台窗口（cmd 样式），在线初始化期间不展示可操作的 GUI，
/// 实时打印获取进度、当前状态与预估剩余时间；完成后由宿主关闭控制台并进入主界面。
/// 用户关闭控制台窗口会终止进程，属预期行为（下次启动按数据库状态继续）。
/// </summary>
public static class FirstRunConsole
{
    public static async Task<FirstRunConsoleResult> RunAsync(IFirstRunInitializer initializer, ISyncService syncService)
    {
        if (!ConsoleNative.AllocConsole())
        {
            return FirstRunConsoleResult.ConsoleUnavailable;
        }

        try
        {
            return await RunCoreAsync(initializer, syncService);
        }
        finally
        {
            ConsoleNative.FreeConsole();
        }
    }

    private static async Task<FirstRunConsoleResult> RunCoreAsync(IFirstRunInitializer initializer, ISyncService syncService)
    {
        ConsoleNative.SetConsoleOutputCP(65001);
        DisableQuickEdit();
        ConsoleNative.SetConsoleTitle("米家产品示例图库 - 首次加载中（请勿关闭）");
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });

        var renderer = new Renderer();
        renderer.WriteHeader();

        void OnEngineProgress(SyncProgress progress) => renderer.OnSyncProgress(progress);
        syncService.ProgressChanged += OnEngineProgress;
        try
        {
            renderer.WriteLine("正在连接米家百科…");
            var report = await initializer.InitializeAsync(null, renderer);
            return report.Outcome switch
            {
                InitializationOutcome.CompletedFromSeed
                    or InitializationOutcome.CompletedOnline
                    or InitializationOutcome.AlreadyInitialized => Success(renderer, report),
                _ => renderer.WriteFailure(report.Message ?? "初始化未完成"),
            };
        }
        catch (OperationCanceledException)
        {
            return renderer.WriteFailure("初始化已取消");
        }
        catch (Exception exception)
        {
            return renderer.WriteFailure(exception.Message);
        }
        finally
        {
            syncService.ProgressChanged -= OnEngineProgress;
        }

        static FirstRunConsoleResult Success(Renderer renderer, InitializationReport report)
        {
            renderer.WriteSuccess(report);
            return FirstRunConsoleResult.Succeeded;
        }
    }

    private static void DisableQuickEdit()
    {
        // 关闭快速编辑模式：用户在控制台选中文本会阻塞进度输出。
        var handle = ConsoleNative.GetStdHandle(ConsoleNative.StdInputHandle);
        if (!ConsoleNative.GetConsoleMode(handle, out var mode))
        {
            return;
        }

        _ = ConsoleNative.SetConsoleMode(handle, (mode & ~ConsoleNative.EnableQuickEditMode) | ConsoleNative.EnableExtendedFlags);
    }

    /// <summary>进度渲染器：阶段切换换行打标题，阶段内单行刷新（\r 覆写）并节流。</summary>
    private sealed class Renderer : IProgress<SeedImportProgress>
    {
        private readonly object gate = new();
        private readonly Stopwatch stageWatch = new();
        private string currentStageKey = string.Empty;

        public void WriteLine(string text)
        {
            Console.Out.WriteLine(text);
        }

        public void WriteHeader()
        {
            WriteLine("======================================================================");
            WriteLine("  米家产品示例图库 · 首次启动初始化");
            WriteLine("======================================================================");
            WriteLine("  数据来源：米家百科公开产品库（只读，不修改官网数据）");
            WriteLine("  预计获取：产品 10,000+ 个 · 示例样图 10,000+ 张");
            WriteLine("  预计耗时：约 5-15 分钟（取决于网络速度），请耐心等待");
            WriteLine("  说明：加载期间不显示应用界面，完成后自动进入图库。");
            WriteLine("        请勿关闭本窗口，关闭会中止本次初始化（重新启动应用可继续）。");
            WriteLine("======================================================================");
        }

        /// <summary>在线同步进度（引擎工作线程触发）。</summary>
        public void OnSyncProgress(SyncProgress progress)
        {
            lock (gate)
            {
                if (progress.Status == SyncStatus.Failed)
                {
                    EndStageLine();
                    Foreground(ConsoleColor.Red, () => WriteLine($"同步阶段失败：{progress.ErrorMessage}"));
                    return;
                }

                if (progress.Status != SyncStatus.Running)
                {
                    return; // 成功收口由初始化报告统一输出，避免重复。
                }

                var stageName = progress.StageIndex > 0
                    ? $"[{progress.StageIndex}/{SyncProgress.TotalStages}] {StageName(progress.Stage)}"
                    : StageName(progress.Stage);
                var detail = progress.Total > 0
                    ? $"{progress.Done:N0}/{progress.Total:N0} ({100.0 * progress.Done / progress.Total:0.0}%)"
                    : progress.Detail ?? string.Empty;
                UpdateStageLine($"sync:{progress.Stage}", stageName, detail, progress.Done, progress.Total, progress.ElapsedSeconds, progress.Failures);
            }
        }

        /// <summary>种子包导入进度（导入线程直接调用，非 UI 上下文）。</summary>
        public void Report(SeedImportProgress value)
        {
            lock (gate)
            {
                var stageName = value.Stage switch
                {
                    SeedImportStage.Validating => "[1/4] 校验种子包",
                    SeedImportStage.CopyingImages => "[2/4] 导入图片",
                    SeedImportStage.WritingDatabase => "[3/4] 写入产品数据",
                    SeedImportStage.Finalizing => "[4/4] 完成初始化",
                    _ => value.Stage.ToString(),
                };
                var (done, total) = value.Stage switch
                {
                    SeedImportStage.CopyingImages => (value.ImagesDone, value.ImagesTotal),
                    SeedImportStage.WritingDatabase => (value.ProductsDone, value.ProductsTotal),
                    _ => (0, 0),
                };
                var detail = total > 0 ? $"{done:N0}/{total:N0} ({100.0 * done / total:0.0}%)" : string.Empty;
                UpdateStageLine($"seed:{value.Stage}", stageName, detail, done, total, stageWatch.Elapsed.TotalSeconds, 0);
            }
        }

        /// <summary>失败收口：打印原因并返回 Failed 结果。</summary>
        public FirstRunConsoleResult WriteFailure(string message)
        {
            lock (gate)
            {
                EndStageLine();
                Foreground(ConsoleColor.Red, () => WriteLine($"初始化失败：{message}"));
                WriteLine("即将打开应用界面，可重试或选择本地种子包…");
                return FirstRunConsoleResult.Failed;
            }
        }

        /// <summary>成功收口。</summary>
        public void WriteSuccess(InitializationReport report)
        {
            lock (gate)
            {
                EndStageLine();
                var summary = report.Outcome == InitializationOutcome.CompletedFromSeed
                    ? $"种子导入完成（快照 {report.SnapshotDate ?? "未知"}）"
                    : report.SnapshotDate is { } snapshot
                        ? $"在线获取完成 · 快照 {snapshot}"
                        : "在线获取完成";
                Foreground(ConsoleColor.Green, () => WriteLine($"初始化完成：{summary}"));
                WriteLine("正在启动应用界面…");
            }
        }

        private void UpdateStageLine(
            string stageKey,
            string stageName,
            string detail,
            int done,
            int total,
            double elapsedSeconds,
            int failures)
        {
            if (stageKey != currentStageKey)
            {
                EndStageLine();
                currentStageKey = stageKey;
                stageWatch.Restart();
                WriteLine(stageName);
                return;
            }

            // 阶段内单行刷新节流约 10Hz，避免控制台输出成为瓶颈。
            if (stageWatch.ElapsedMilliseconds < 100)
            {
                return;
            }

            stageWatch.Restart();
            var parts = new List<string>();
            if (detail.Length > 0)
            {
                parts.Add(detail);
            }

            if (total > 0)
            {
                parts.Add($"已用 {SyncEta.FormatClock(elapsedSeconds)}");
                if (SyncEta.Estimate(done, total, elapsedSeconds) is { } remaining)
                {
                    parts.Add($"剩余约 {SyncEta.Format(remaining)}");
                }
            }

            if (failures > 0)
            {
                parts.Add($"图片失败 {failures}");
            }

            if (parts.Count > 0)
            {
                Console.Out.Write($"\r    {string.Join(" · ", parts)}                    ");
            }
        }

        private void EndStageLine()
        {
            if (currentStageKey.Length == 0)
            {
                return;
            }

            currentStageKey = string.Empty;
            Console.Out.WriteLine();
        }

        private static void Foreground(ConsoleColor color, Action write)
        {
            var original = Console.ForegroundColor;
            Console.ForegroundColor = color;
            try
            {
                write();
            }
            finally
            {
                Console.ForegroundColor = original;
            }
        }

        private static string StageName(SyncStage stage)
        {
            return stage switch
            {
                SyncStage.FetchingCategories => "获取分类",
                SyncStage.FetchingProducts => "获取产品",
                SyncStage.Comparing => "对比数据",
                SyncStage.DownloadingImages => "下载图片",
                SyncStage.UpdatingDatabase => "更新数据库",
                SyncStage.GeneratingThumbnails => "生成缩略图",
                SyncStage.Completed => "完成",
                _ => stage.ToString(),
            };
        }
    }
}

/// <summary>控制台窗口 Win32 接口。</summary>
internal static class ConsoleNative
{
    internal const int StdInputHandle = -10;
    internal const uint EnableQuickEditMode = 0x0040;
    internal const uint EnableExtendedFlags = 0x0080;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FreeConsole();

    [DllImport("kernel32.dll", EntryPoint = "SetConsoleTitleW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetConsoleTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint SetConsoleOutputCP(uint codePage);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
