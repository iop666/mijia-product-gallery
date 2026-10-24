using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Rules;
using MijiaProductGallery.Infrastructure.Database;
using SkiaSharp;

namespace MijiaProductGallery.Infrastructure.Images;

/// <summary>缩略图参数（运行时可变，设置页更新）。</summary>
public sealed class ThumbnailSettings
{
    /// <summary>缩略图最长边（像素）。</summary>
    public int MaxEdge { get; set; } = 480;

    /// <summary>WebP 编码质量（0-100）。</summary>
    public int Quality { get; set; } = 80;
}

/// <summary>
/// 缩略图缓存：{安全主名}.{sha8}.webp。SHA 绑定使原图更换自动指向新缓存名；
/// 命中时校验可解码，损坏即删除重建；任何操作只读原图。
/// </summary>
public sealed class ThumbnailService(DatabasePaths paths, ThumbnailSettings? settings = null) : IThumbnailService
{
    private readonly ThumbnailSettings options = settings ?? new ThumbnailSettings();

    /// <summary>当前参数（设置页可更新，即时生效）。</summary>
    public ThumbnailSettings Options => options;

    public Task<string> EnsureThumbnailAsync(string imageFileName, string sha256, CancellationToken cancellationToken = default)
    {
        var thumbnailPath = ThumbnailPath(imageFileName, sha256);
        if (File.Exists(thumbnailPath))
        {
            if (IsDecodable(thumbnailPath))
            {
                return Task.FromResult(thumbnailPath);
            }

            // 缓存损坏自愈：删除后由原图重建。
            File.Delete(thumbnailPath);
        }

        Directory.CreateDirectory(paths.ThumbnailsDirectory);
        var originalPath = Path.Combine(
            paths.ImagesDirectory,
            FileNameRules.ToDiskFileName(imageFileName));
        if (!File.Exists(originalPath))
        {
            throw new FileNotFoundException("原图缺失，无法生成缩略图", originalPath);
        }

        using var bitmap = SKBitmap.Decode(originalPath);
        if (bitmap is null)
        {
            throw new ImageValidationException("原图无法解码，缩略图生成失败");
        }

        using var scaled = ScaleIfNeeded(bitmap);
        using var image = SKImage.FromBitmap(scaled);
        using var data = image.Encode(SKEncodedImageFormat.Webp, options.Quality);
        var bytes = data.ToArray();

        var temp = Path.Combine(paths.ThumbnailsDirectory, $".tmp-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, thumbnailPath, overwrite: true);
        }
        catch (IOException) when (File.Exists(thumbnailPath))
        {
            // 另一张卡片/另一个进程刚写入同名缓存：目标已存在即视为成功。
            TryDelete(temp);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        return Task.FromResult(thumbnailPath);
    }

    public Task DeleteThumbnailAsync(string imageFileName, string sha256, CancellationToken cancellationToken = default)
    {
        var thumbnailPath = ThumbnailPath(imageFileName, sha256);
        if (File.Exists(thumbnailPath))
        {
            File.Delete(thumbnailPath);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAllThumbnailsAsync(CancellationToken cancellationToken = default)
    {
        // 逐个删除：界面上仍在显示的缩略图可能被 BitmapImage 占用，跳过即可，不能让整次清空失败。
        return Task.Run(() =>
        {
            Directory.CreateDirectory(paths.ThumbnailsDirectory);
            foreach (var file in Directory.EnumerateFiles(paths.ThumbnailsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    File.Delete(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }, cancellationToken);
    }

    private string ThumbnailPath(string imageFileName, string sha256)
    {
        var safeBase = Path.GetFileNameWithoutExtension(FileNameRules.ToDiskFileName(imageFileName));
        return Path.Combine(paths.ThumbnailsDirectory, $"{safeBase}.{sha256[..8]}.webp");
    }

    private SKBitmap ScaleIfNeeded(SKBitmap bitmap)
    {
        var maxEdge = Math.Max(bitmap.Info.Width, bitmap.Info.Height);
        if (maxEdge <= options.MaxEdge)
        {
            return bitmap;
        }

        var ratio = (float)options.MaxEdge / maxEdge;
        var targetWidth = (int)Math.Round(bitmap.Info.Width * ratio);
        var targetHeight = (int)Math.Round(bitmap.Info.Height * ratio);
        var scaled = new SKBitmap(targetWidth, targetHeight);
        bitmap.ScalePixels(scaled, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        return scaled;
    }

    /// <summary>只读文件头判断可解码（SKCodec 不分配像素缓冲，命中缓存时成本远低于全量解码）。</summary>
    private static bool IsDecodable(string path)
    {
        try
        {
            if (new FileInfo(path).Length < 16)
            {
                return false;
            }

            using var codec = SKCodec.Create(path);
            return codec is not null && codec.Info.Width > 0 && codec.Info.Height > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
