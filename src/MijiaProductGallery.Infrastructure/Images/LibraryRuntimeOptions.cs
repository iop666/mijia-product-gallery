using MijiaProductGallery.Core;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;

namespace MijiaProductGallery.Infrastructure.Images;

/// <summary>
/// 图库运行时参数实现：启动时从 AppSettings 读取，设置页更新后即时生效并持久化。
/// </summary>
public sealed class LibraryRuntimeOptions(
    ISettingsRepository settingsRepository,
    ThumbnailSettings thumbnailSettings) : ILibraryRuntimeOptions
{
    public int ThumbMaxEdge { get; private set; } = AppSettingsKeys.ThumbMaxEdgeDefault;

    public int ThumbQuality { get; private set; } = AppSettingsKeys.ThumbQualityDefault;

    /// <summary>启动时从 AppSettings 读取参数（应用启动后调用一次）。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        ThumbMaxEdge = Clamp(
            await settingsRepository.GetValueAsync(AppSettingsKeys.ThumbMaxEdge, AppSettingsKeys.ThumbMaxEdgeDefault, cancellationToken),
            120, 960);
        ThumbQuality = Clamp(
            await settingsRepository.GetValueAsync(AppSettingsKeys.ThumbQuality, AppSettingsKeys.ThumbQualityDefault, cancellationToken),
            40, 100);
        WriteThrough();
    }

    public async Task UpdateAsync(int thumbMaxEdge, int thumbQuality, CancellationToken cancellationToken = default)
    {
        ThumbMaxEdge = Clamp(thumbMaxEdge, 120, 960);
        ThumbQuality = Clamp(thumbQuality, 40, 100);
        await settingsRepository.SetValueAsync(AppSettingsKeys.ThumbMaxEdge, ThumbMaxEdge, cancellationToken);
        await settingsRepository.SetValueAsync(AppSettingsKeys.ThumbQuality, ThumbQuality, cancellationToken);
        WriteThrough();
    }

    /// <summary>把当前参数同步到共享缩略图设置实例（ThumbnailService 每次生成都读取）。</summary>
    private void WriteThrough()
    {
        thumbnailSettings.MaxEdge = ThumbMaxEdge;
        thumbnailSettings.Quality = ThumbQuality;
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Clamp(value, min, max);
    }
}
