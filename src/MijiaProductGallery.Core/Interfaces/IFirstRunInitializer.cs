using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Core.Interfaces;

/// <summary>
/// 首次启动初始化契约：检测本地数据库 → 检测 Seed Package → 种子导入；
/// 无种子时探测网络并走在线初始化；无种子且离线时返回显式失败状态（不伪装成正常空图库）。
/// </summary>
public interface IFirstRunInitializer
{
    /// <summary>
    /// 执行首次初始化。explicitSeedPath 为 null 时按约定位置查找（exe 目录 → 数据根 seed\ 下 seed-*.zip）。
    /// </summary>
    Task<InitializationReport> InitializeAsync(
        string? explicitSeedPath = null,
        IProgress<SeedImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
