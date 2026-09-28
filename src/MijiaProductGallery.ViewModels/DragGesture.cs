namespace MijiaProductGallery.ViewModels;

/// <summary>
/// 拖拽手势与策略（纯函数）：长按或位移达到阈值即进入拖拽；
/// 无图型号一律禁止拖拽，绝不生成空文件。
/// </summary>
public static class DragGesture
{
    /// <summary>位移触发阈值（逻辑像素）。</summary>
    public const double MoveThreshold = 12.0;

    /// <summary>长按触发阈值（毫秒）。</summary>
    public const int HoldThresholdMilliseconds = 400;

    /// <summary>按住期间位移是否已达拖拽触发条件。</summary>
    public static bool ShouldStartByMove(double deltaX, double deltaY)
    {
        return Math.Abs(deltaX) >= MoveThreshold || Math.Abs(deltaY) >= MoveThreshold;
    }

    /// <summary>按住时长是否已达长按触发条件。</summary>
    public static bool ShouldStartByHold(int heldMilliseconds)
    {
        return heldMilliseconds >= HoldThresholdMilliseconds;
    }

    /// <summary>该卡片是否允许发起拖拽（无图型号不允许）。</summary>
    public static bool CanDrag(ProductCard card)
    {
        return card.HasImage && card.ImagePath is not null;
    }
}
