using CommunityToolkit.Mvvm.ComponentModel;
using MijiaProductGallery.Core;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.ViewModels;

/// <summary>
/// 同步中心视图模型：状态/阶段/上次同步/变更计数展示与手动触发、取消、自动频率设置。
/// 订阅同步引擎实时进度（工作线程触发，经调度器回投 UI 线程），展示阶段/百分比/计数与剩余时间。
/// </summary>
public partial class SyncCenterViewModel : ObservableObject
{
    public const string AutoIntervalSettingsKey = "Sync.AutoInterval";

    private readonly ISyncService syncService;
    private readonly ISyncStateRepository syncStateRepository;
    private readonly ISettingsRepository settings;
    private readonly IUiDispatcher? uiDispatcher;

    public SyncCenterViewModel(
        ISyncService syncService,
        ISyncStateRepository syncStateRepository,
        ISettingsRepository settings,
        IUiDispatcher? uiDispatcher = null)
    {
        this.syncService = syncService;
        this.syncStateRepository = syncStateRepository;
        this.settings = settings;
        this.uiDispatcher = uiDispatcher;
        syncService.ProgressChanged += OnEngineProgress;
    }

    /// <summary>是否正在同步（驱动按钮可用性与进度区可见性）。</summary>
    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private string statusText = "空闲";

    [ObservableProperty]
    private string stageText = string.Empty;

    [ObservableProperty]
    private string lastSyncText = "从未同步";

    [ObservableProperty]
    private string? errorMessage;

    /// <summary>最近一轮变更统计摘要（如"新增 3 · 下架 1"），无变更时为空。</summary>
    [ObservableProperty]
    private string? lastRunSummary;

    /// <summary>进度区阶段文本（"阶段 4/6 · 下载图片"）。</summary>
    [ObservableProperty]
    private string progressStageText = string.Empty;

    /// <summary>全流程估算百分比（0-100）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercentText))]
    private double progressPercent;

    /// <summary>无总量可估时进度条为流动态。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercentText))]
    private bool isProgressIndeterminate;

    /// <summary>进度明细（计数 · 已用 · 剩余 · 失败数）。</summary>
    [ObservableProperty]
    private string progressDetailText = string.Empty;

    [ObservableProperty]
    private SyncAutoInterval autoInterval = SyncAutoInterval.Daily;

    [ObservableProperty]
    private bool isLoadingSettings = true;

    /// <summary>百分比文本（流动态不显示）。</summary>
    public string ProgressPercentText => IsProgressIndeterminate ? string.Empty : $"{ProgressPercent:0}%";

    /// <summary>AutoIntervalIndex 绑定值（ComboBox SelectedIndex 0-4）；赋值即持久化（不等待完成）。</summary>
    public int AutoIntervalIndex
    {
        get => (int)AutoInterval;
        set
        {
            AutoInterval = (SyncAutoInterval)value;
            _ = SetAutoIntervalAsync((SyncAutoInterval)value);
        }
    }

    /// <summary>是否有错误可展示。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>是否有最近一轮摘要可展示。</summary>
    public bool HasLastRunSummary => !string.IsNullOrEmpty(LastRunSummary);

    /// <summary>触发一次手动同步（立即同步与重试共用）。</summary>
    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        try
        {
            _ = await syncService.SyncNowAsync(SyncTrigger.Manual, cancellationToken);
        }
        finally
        {
            await RefreshAsync(cancellationToken);
            IsRunning = false;
        }
    }

    /// <summary>请求取消当前同步（引擎将本轮标记失败，可重试）。</summary>
    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            return;
        }

        await syncService.CancelAsync(cancellationToken);
    }

    /// <summary>读取同步状态与最近一轮记录（供周期轮询）。</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var state = await syncStateRepository.GetStateAsync(cancellationToken);
        IsRunning = state.Status == SyncStatus.Running;
        StatusText = state.Status switch
        {
            SyncStatus.Idle => "空闲",
            SyncStatus.Running => "同步中",
            SyncStatus.Success => "成功",
            SyncStatus.Failed => "失败",
            _ => state.Status.ToString(),
        };
        StageText = StageName(state.Stage);
        ErrorMessage = state.ErrorMessage;
        OnPropertyChanged(nameof(HasError));
        LastSyncText = state.LastSyncUnix is null
            ? "从未同步"
            : $"本地 {DateTimeOffset.FromUnixTimeSeconds(state.LastSyncUnix.Value).ToLocalTime():yyyy-MM-dd HH:mm}";

        if (!IsRunning)
        {
            ProgressDetailText = string.Empty;
        }

        var latestRun = await syncStateRepository.GetLatestRunAsync(cancellationToken);
        if (latestRun?.Counts is { } counts)
        {
            var parts = new List<string>();
            if (counts.NewCount > 0) parts.Add($"新增 {counts.NewCount}");
            if (counts.NameChangedCount > 0) parts.Add($"改名 {counts.NameChangedCount}");
            if (counts.ImageChangedCount > 0) parts.Add($"换图 {counts.ImageChangedCount}");
            if (counts.IdReusedCount > 0) parts.Add($"ID复用 {counts.IdReusedCount}");
            if (counts.DelistedCount > 0) parts.Add($"下架 {counts.DelistedCount}");
            if (counts.ImageFailureCount > 0) parts.Add($"图片失败 {counts.ImageFailureCount}");
            LastRunSummary = parts.Count == 0 ? "无变更" : string.Join(" · ", parts);
        }
        else
        {
            LastRunSummary = null;
        }

        OnPropertyChanged(nameof(HasLastRunSummary));
    }

    /// <summary>加载自动同步频率设置（页面进入时）。</summary>
    public async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        var text = await settings.GetValueAsync(AutoIntervalSettingsKey, nameof(SyncAutoInterval.Daily), cancellationToken);
        AutoInterval = Enum.TryParse<SyncAutoInterval>(text, ignoreCase: true, out var parsed)
            ? parsed
            : SyncAutoInterval.Daily;
        IsLoadingSettings = false;
    }

    /// <summary>保存自动同步频率设置。</summary>
    public async Task SetAutoIntervalAsync(SyncAutoInterval interval, CancellationToken cancellationToken = default)
    {
        AutoInterval = interval;
        await settings.SetValueAsync(AutoIntervalSettingsKey, interval.ToString(), cancellationToken);
    }

    /// <summary>引擎进度回调（引擎线程触发）→ 调度到 UI 线程更新。</summary>
    private void OnEngineProgress(SyncProgress progress)
    {
        if (uiDispatcher is { } dispatcher)
        {
            dispatcher.Post(() => ApplyProgress(progress));
        }
        else
        {
            ApplyProgress(progress);
        }
    }

    private void ApplyProgress(SyncProgress progress)
    {
        if (progress.Status != SyncStatus.Running)
        {
            // 终态由轮询 RefreshAsync 收口；此处仅复位进度条形态。
            IsProgressIndeterminate = false;
            ProgressPercent = progress.Status == SyncStatus.Success ? 100 : 0;
            return;
        }

        IsRunning = true;
        StatusText = "同步中";
        StageText = StageName(progress.Stage);
        var stageLabel = progress.StageIndex > 0
            ? $"阶段 {progress.StageIndex}/{SyncProgress.TotalStages} · {StageName(progress.Stage)}"
            : StageName(progress.Stage);
        ProgressStageText = progress.Detail is null ? stageLabel : $"{stageLabel} · {progress.Detail}";
        if (progress.OverallPercent is { } percent)
        {
            IsProgressIndeterminate = false;
            ProgressPercent = percent;
        }
        else
        {
            IsProgressIndeterminate = true;
        }

        var eta = SyncEta.Estimate(progress.Done, progress.Total, progress.ElapsedSeconds);
        var parts = new List<string>();
        if (progress.Total > 0)
        {
            parts.Add($"{progress.Done:N0}/{progress.Total:N0}");
        }

        parts.Add($"已用 {SyncEta.FormatClock(progress.ElapsedSeconds)}");
        if (eta is { } remaining)
        {
            parts.Add($"剩余约 {SyncEta.Format(remaining)}");
        }

        if (progress.Failures > 0)
        {
            parts.Add($"图片失败 {progress.Failures}");
        }

        ProgressDetailText = string.Join(" · ", parts);
    }

    /// <summary>阶段中文名（同步中心与实时进度共用）。</summary>
    private static string StageName(SyncStage stage)
    {
        return stage switch
        {
            SyncStage.NotStarted => string.Empty,
            SyncStage.FetchingCategories => "获取分类",
            SyncStage.FetchingProducts => "获取产品",
            SyncStage.Comparing => "对比数据",
            SyncStage.DownloadingImages => "下载图片",
            SyncStage.GeneratingThumbnails => "生成缩略图",
            SyncStage.UpdatingDatabase => "更新数据库",
            SyncStage.Completed => "完成",
            _ => string.Empty,
        };
    }
}
