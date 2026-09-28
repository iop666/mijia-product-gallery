using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.ViewModels;

/// <summary>初始化页状态。</summary>
public enum InitializationUiState
{
    /// <summary>初始化进行中。</summary>
    Running,

    /// <summary>初始化成功（可进入图库）。</summary>
    Succeeded,

    /// <summary>初始化失败（展示原因与可用操作）。</summary>
    Failed,
}

/// <summary>
/// 首次初始化页视图模型：执行 FirstRunInitializer（数据库检查 → 种子导入 → 在线兜底），
/// 只负责状态展示与重试/选种子入口，不承载导入逻辑。
/// </summary>
public partial class InitializationViewModel : ObservableObject
{
    private readonly IFirstRunInitializer initializer;

    public InitializationViewModel(IFirstRunInitializer initializer)
    {
        this.initializer = initializer;
    }

    /// <summary>初始化成功后触发（UI 侧跳转图库）。</summary>
    public event Action? Succeeded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFailed), nameof(HasError))]
    private InitializationUiState state = InitializationUiState.Running;

    [ObservableProperty]
    private string stageText = "正在准备…";

    [ObservableProperty]
    private double progressValue;

    /// <summary>进度不确定（如读取数据库、在线同步）时进度条为流动态。</summary>
    [ObservableProperty]
    private bool isIndeterminate = true;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private IReadOnlyList<string> availableActions = [];

    [ObservableProperty]
    private bool canPickSeed;

    /// <summary>失败态可见性（供 x:Bind 直绑）。</summary>
    public bool IsFailed => State == InitializationUiState.Failed;

    /// <summary>是否存在错误信息（供 InfoBar 直绑）。</summary>
    public bool HasError => State == InitializationUiState.Failed && ErrorMessage is not null;

    /// <summary>执行初始化。explicitSeedPath 非空时跳过自动查找。</summary>
    public async Task RunAsync(string? explicitSeedPath = null, CancellationToken cancellationToken = default)
    {
        State = InitializationUiState.Running;
        ErrorMessage = null;
        AvailableActions = [];
        IsIndeterminate = true;
        StageText = "正在检查本地数据库…";
        try
        {
            var progress = new Progress<SeedImportProgress>(OnProgress);
            var report = await initializer.InitializeAsync(explicitSeedPath, progress, cancellationToken);
            if (report.Outcome is InitializationOutcome.CompletedFromSeed
                or InitializationOutcome.CompletedOnline
                or InitializationOutcome.AlreadyInitialized)
            {
                StageText = report.Outcome == InitializationOutcome.CompletedFromSeed
                    ? $"种子导入完成（快照 {report.SnapshotDate}）"
                    : "初始化完成";
                ProgressValue = 100;
                IsIndeterminate = false;
                State = InitializationUiState.Succeeded;
                Succeeded?.Invoke();
                return;
            }

            ShowFailure(report.Message ?? "初始化未完成", report.AvailableActions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            ShowFailure(exception.Message, ["retry", "check-seed", "online-init"]);
        }
    }

    /// <summary>用户选择种子包文件后重试。</summary>
    public Task RetryWithSeedAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        return RunAsync(packagePath, cancellationToken);
    }

    private void ShowFailure(string message, IReadOnlyList<string> actions)
    {
        State = InitializationUiState.Failed;
        IsIndeterminate = false;
        ProgressValue = 0;
        ErrorMessage = message;
        AvailableActions = actions;
        CanPickSeed = actions.Contains("check-seed");
    }

    private void OnProgress(SeedImportProgress progress)
    {
        StageText = progress.Stage switch
        {
            SeedImportStage.Validating => "正在校验种子包…",
            SeedImportStage.CopyingImages => $"正在导入图片 {progress.ImagesDone}/{progress.ImagesTotal}…",
            SeedImportStage.WritingDatabase => $"正在写入产品数据 {progress.ProductsDone}/{progress.ProductsTotal}…",
            SeedImportStage.Finalizing => "正在完成初始化…",
            _ => progress.Stage.ToString(),
        };
        IsIndeterminate = false;
        if (progress.ImagesTotal > 0)
        {
            ProgressValue = 100.0 * progress.ImagesDone / progress.ImagesTotal;
        }
        else if (progress.ProductsTotal > 0)
        {
            ProgressValue = 100.0 * progress.ProductsDone / progress.ProductsTotal;
        }
    }
}
