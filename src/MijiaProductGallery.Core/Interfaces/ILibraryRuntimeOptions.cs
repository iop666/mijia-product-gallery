namespace MijiaProductGallery.Core.Interfaces;

/// <summary>
/// 图库运行时参数（缩略图尺寸/质量）：由设置页更新、缩略图服务即时读取。
/// 实现为可变单例，生命周期与应用一致。
/// </summary>
public interface ILibraryRuntimeOptions
{
    /// <summary>缩略图最长边（像素）。</summary>
    int ThumbMaxEdge { get; }

    /// <summary>WebP 编码质量（0-100）。</summary>
    int ThumbQuality { get; }

    /// <summary>更新参数（同时负责持久化）。</summary>
    Task UpdateAsync(int thumbMaxEdge, int thumbQuality, CancellationToken cancellationToken = default);
}
