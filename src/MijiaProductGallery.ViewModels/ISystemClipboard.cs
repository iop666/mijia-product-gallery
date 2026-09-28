namespace MijiaProductGallery.ViewModels;

/// <summary>系统剪贴板抽象（App 层以 Windows Clipboard 实现，测试用桩）。</summary>
public interface ISystemClipboard
{
    /// <summary>写入位图 + 文件路径双格式；失败返回 false。</summary>
    Task<bool> SetImageFileAsync(string absolutePath, CancellationToken cancellationToken = default);

    /// <summary>写入纯文本；失败返回 false。</summary>
    Task<bool> SetTextAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>卡片操作结果。</summary>
public sealed record CardActionResult(bool Success, string? ErrorMessage = null)
{
    public static CardActionResult Ok() => new(true);

    public static CardActionResult Fail(string message) => new(false, message);
}
