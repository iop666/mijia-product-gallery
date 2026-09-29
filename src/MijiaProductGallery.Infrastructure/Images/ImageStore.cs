using System.Security.Cryptography;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Rules;
using MijiaProductGallery.Infrastructure.Database;
using SkiaSharp;

namespace MijiaProductGallery.Infrastructure.Images;

/// <summary>
/// 原图文件存储：内容缓冲 → 空文件/魔数/完整解码校验 → SHA256 → 临时文件 → 原子落位。
/// 三个入口对应对比规则的三种图片结论；验证失败时既有文件保持字节级不变。
/// </summary>
public sealed class ImageStore(DatabasePaths paths) : IImageStore
{
    public async Task<ImageStoreResult> StoreNewAsync(string model, Stream content, CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(content, cancellationToken);
        var fileName = model + ImageHeaderRules.GetExtension(validated.Format);
        var target = TargetPath(fileName);

        var replacingOrphan = false;
        if (File.Exists(target))
        {
            var existingSha = await ComputeFileSha256Async(target, cancellationToken);
            if (string.Equals(existingSha, validated.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Result(ImageStoreStatus.Unchanged, fileName, validated);
            }

            // 数据库无原图记录的孤儿同名文件：按验证后替换处理，无从留史。
            replacingOrphan = true;
        }

        await PlaceAtomicallyAsync(target, validated.Bytes, cancellationToken);
        return Result(
            replacingOrphan ? ImageStoreStatus.Replaced : ImageStoreStatus.StoredNew,
            fileName,
            validated);
    }

    public async Task<ImageStoreResult> ReplaceAsync(string model, string currentImageFileName, Stream content, CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(content, cancellationToken);
        var fileName = model + ImageHeaderRules.GetExtension(validated.Format);
        var target = TargetPath(fileName);

        if (File.Exists(target))
        {
            var existingSha = await ComputeFileSha256Async(target, cancellationToken);
            if (string.Equals(existingSha, validated.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Result(ImageStoreStatus.Unchanged, fileName, validated);
            }
        }

        await PlaceAtomicallyAsync(target, validated.Bytes, cancellationToken);
        if (!string.Equals(fileName, currentImageFileName, StringComparison.Ordinal))
        {
            // 官方换图可能同时更换扩展名：清掉旧扩展名文件，避免孤儿原图。
            DeleteIfExists(TargetPath(currentImageFileName));
        }

        return Result(ImageStoreStatus.Replaced, fileName, validated);
    }

    public async Task<ImageStoreResult> ReplaceWithHistoryAsync(string model, string currentImageFileName, Stream content, CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(content, cancellationToken);
        var fileName = model + ImageHeaderRules.GetExtension(validated.Format);
        var currentDisk = TargetPath(currentImageFileName);

        if (!File.Exists(currentDisk))
        {
            // 无图型号补图：没有可保留的历史，退化为首次入库。
            await PlaceAtomicallyAsync(TargetPath(fileName), validated.Bytes, cancellationToken);
            return Result(ImageStoreStatus.StoredNew, fileName, validated);
        }

        if (string.Equals(fileName, currentImageFileName, StringComparison.Ordinal))
        {
            var existingSha = await ComputeFileSha256Async(currentDisk, cancellationToken);
            if (string.Equals(existingSha, validated.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Result(ImageStoreStatus.Unchanged, fileName, validated);
            }
        }

        // 新内容验证通过后才移动旧图：旧图让位进 .old 链链尾，新图占用原文件名。
        var generation = FileNameRules.GetNextOldGeneration(ScanExistingOldFiles(currentDisk));
        var oldDiskName = ShiftToOldName(Path.GetFileName(currentDisk), generation);
        var oldTarget = Path.Combine(paths.ImagesDirectory, oldDiskName);
        File.Move(currentDisk, oldTarget);

        await PlaceAtomicallyAsync(TargetPath(fileName), validated.Bytes, cancellationToken);

        return new ImageStoreResult
        {
            Status = ImageStoreStatus.ReplacedWithHistory,
            ImageFileName = fileName,
            ImagePath = RelativePath(FileNameRules.ToDiskFileName(fileName)),
            Format = validated.Format,
            Width = validated.Width,
            Height = validated.Height,
            FileSize = validated.FileSize,
            Sha256 = validated.Sha256,
            ReplacedByOldFileName = FileNameRules.GetOldImageFileName(currentImageFileName, generation),
        };
    }

    public string? ResolveAbsolutePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var rootFull = Path.GetFullPath(paths.Root);
        var resolved = Path.GetFullPath(Path.Combine(rootFull, imagePath));
        return resolved.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? resolved
            : null;
    }

    private static async Task<ValidatedImage> ValidateAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken)) > 0)
        {
            total += read;
            if (total > ImageHeaderRules.MaxImageLength)
            {
                throw new ImageValidationException($"图片超过大小上限 {ImageHeaderRules.MaxImageLength} 字节");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        var bytes = buffer.ToArray();
        if (bytes.Length == 0)
        {
            throw new ImageValidationException("图片内容为空（0 字节）");
        }

        var format = ImageHeaderRules.DetectFormat(bytes);
        if (format == ImageFormat.Unknown)
        {
            throw new ImageValidationException("文件头无法识别为受支持的图片格式（PNG/JPEG/GIF/WEBP）");
        }

        using var bitmap = StrictDecode(bytes);
        if (bitmap is null || bitmap.Info.Width <= 0 || bitmap.Info.Height <= 0)
        {
            throw new ImageValidationException("图片无法完整解码（伪图、截断或损坏）");
        }

        return new ValidatedImage(
            bytes,
            format,
            bitmap.Info.Width,
            bitmap.Info.Height,
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    /// <summary>严格解码：魔数之后要求所有数据块完整、全图可解，PartialDecode 一律拒绝。</summary>
    private static SKBitmap? StrictDecode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return null;
        }

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, codec.Info.ColorType, codec.Info.AlphaType);
        var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }

    private static async Task PlaceAtomicallyAsync(string target, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = Path.Combine(
            Path.GetDirectoryName(target)!,
            $".tmp-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
            }

            throw;
        }
    }

    private string[] ScanExistingOldFiles(string currentDiskFile)
    {
        var baseName = Path.GetFileNameWithoutExtension(currentDiskFile);
        var extension = Path.GetExtension(currentDiskFile);
        if (!Directory.Exists(paths.ImagesDirectory))
        {
            return [];
        }

        return Directory.GetFiles(paths.ImagesDirectory, baseName + ".old*" + extension)
            .Select(Path.GetFileName)
            .Where(name => name is not null && FileNameRules.TryGetOldGeneration(name, out _))
            .Select(name => name!)
            .ToArray();
    }

    private static string ShiftToOldName(string diskFileName, int generation)
    {
        var baseName = Path.GetFileNameWithoutExtension(diskFileName);
        var extension = Path.GetExtension(diskFileName);
        return generation == 0
            ? $"{baseName}.old{extension}"
            : $"{baseName}.old{generation}{extension}";
    }

    private string TargetPath(string realFileName)
    {
        return Path.Combine(paths.ImagesDirectory, FileNameRules.ToDiskFileName(realFileName));
    }

    private static string RelativePath(string diskFileName)
    {
        return $"images/{diskFileName}";
    }

    private static ImageStoreResult Result(ImageStoreStatus status, string fileName, ValidatedImage validated)
    {
        return new ImageStoreResult
        {
            Status = status,
            ImageFileName = fileName,
            ImagePath = RelativePath(FileNameRules.ToDiskFileName(fileName)),
            Format = validated.Format,
            Width = validated.Width,
            Height = validated.Height,
            FileSize = validated.FileSize,
            Sha256 = validated.Sha256,
        };
    }

    private async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record ValidatedImage(
        byte[] Bytes,
        ImageFormat Format,
        int Width,
        int Height,
        long FileSize,
        string Sha256);
}
