namespace MijiaProductGallery.ViewModels;

/// <summary>UI 线程调度抽象：实现方把回调投递到 UI 线程。</summary>
public interface IUiDispatcher
{
    /// <summary>在 UI 线程上执行回调。</summary>
    void Post(Action action);
}

/// <summary>测试/非 UI 环境的同步调度器：回调在调用线程直接执行。</summary>
public sealed class InlineUiDispatcher : IUiDispatcher
{
    public static InlineUiDispatcher Instance { get; } = new();

    public void Post(Action action)
    {
        action();
    }
}
