using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Rules;
using SkiaSharp;

namespace MijiaProductGallery.Tests.TestSupport;

/// <summary>一条种子产品记录（图片内容按需携带，写入时自动计算 SHA/尺寸/格式）。</summary>
public sealed class SeedLineFixture
{
    public required string Model { get; init; }

    public required string Name { get; init; }

    public string Brand { get; init; } = "小米出品";

    public required string Category { get; init; }

    public bool IsAvailable { get; init; } = true;

    public string? ImageFileName { get; private set; }

    public byte[]? ImageBytes { get; private set; }

    /// <summary>官方登记有图但包内缺图。</summary>
    public bool ImageMissing { get; init; }

    public List<(string FileName, byte[] Bytes)> OldImages { get; } = [];

    public SeedLineFixture WithImage(string fileName, byte[] bytes)
    {
        ImageFileName = fileName;
        ImageBytes = bytes;
        return this;
    }

    public SeedLineFixture WithOldImage(string fileName, byte[] bytes)
    {
        OldImages.Add((fileName, bytes));
        return this;
    }
}

/// <summary>测试用 seedpack-v1 组装器：生成与真实格式一致的 zip 包。</summary>
public static class SeedPackFixture
{
    public static async Task<string> WriteAsync(
        string outputDirectory,
        string snapshotDate,
        IReadOnlyList<SeedLineFixture> lines,
        Action<ZipArchive>? mutate = null)
    {
        Directory.CreateDirectory(outputDirectory);
        var packagePath = Path.Combine(outputDirectory, $"seed-{snapshotDate}.zip");
        using var packageStream = File.Create(packagePath);
        using var zip = new ZipArchive(packageStream, ZipArchiveMode.Create);

        var jsonl = new StringBuilder();
        var imageCount = 0;
        var imageFileCount = 0;
        foreach (var line in lines)
        {
            var oldImages = line.OldImages
                .Select(old => new { fileName = old.FileName, sha256 = Sha(old.Bytes) })
                .ToList();
            var record = new Dictionary<string, object?>
            {
                ["model"] = line.Model,
                ["name"] = line.Name,
                ["brand"] = line.Brand,
                ["category"] = line.Category,
                ["isAvailable"] = line.IsAvailable,
            };
            if (line.ImageFileName is not null && !line.ImageMissing)
            {
                record["imageFileName"] = line.ImageFileName;
                record["sha256"] = Sha(line.ImageBytes!);
                record["format"] = FormatName(line.ImageBytes!);
                using var bitmap = SKBitmap.Decode(line.ImageBytes);
                record["width"] = bitmap!.Info.Width;
                record["height"] = bitmap.Info.Height;
                record["fileLength"] = line.ImageBytes!.Length;
                imageCount++;
            }
            else if (line.ImageFileName is not null)
            {
                record["imageFileName"] = line.ImageFileName;
                record["imageMissing"] = true;
            }

            if (oldImages.Count > 0)
            {
                record["oldImages"] = oldImages;
            }

            jsonl.AppendLine(JsonSerializer.Serialize(record));

            if (line.ImageBytes is not null && !line.ImageMissing)
            {
                AddEntry(zip, $"images/{line.ImageFileName}", line.ImageBytes);
                imageFileCount++;
            }

            foreach (var (fileName, bytes) in line.OldImages)
            {
                AddEntry(zip, $"images/{fileName}", bytes);
                imageFileCount++;
            }

            if (line.ImageFileName is not null && line.ImageMissing)
            {
                // 缺图产品不产生包内文件。
            }
        }

        var jsonlBytes = Encoding.UTF8.GetBytes(jsonl.ToString());
        AddEntry(zip, "products.jsonl", jsonlBytes);

        var manifest = new
        {
            schemaVersion = 1,
            snapshotDate,
            source = $"iop666/mijia-product-icons@{snapshotDate}",
            productCount = lines.Count,
            imageCount,
            imageFileCount,
            productsSha256 = Convert.ToHexString(SHA256.HashData(jsonlBytes)).ToLowerInvariant(),
            generator = "SeedPackFixture",
        };
        AddEntry(zip, "manifest.json", Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true })));

        // 先结束 Create 模式（写入中央目录），变更操作在 Update 模式下进行。
        await zip.DisposeAsync();
        await packageStream.DisposeAsync();
        if (mutate is not null)
        {
            using var updateStream = File.Open(packagePath, FileMode.Open, FileAccess.ReadWrite);
            using var updateZip = new ZipArchive(updateStream, ZipArchiveMode.Update);
            mutate(updateZip);
        }

        return packagePath;
    }

    private static void AddEntry(ZipArchive zip, string entryName, byte[] bytes)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static string Sha(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string FormatName(byte[] bytes)
    {
        return ImageHeaderRules.DetectFormat(bytes) switch
        {
            ImageFormat.Png => "png",
            ImageFormat.Jpg => "jpg",
            ImageFormat.Gif => "gif",
            ImageFormat.Webp => "webp",
            _ => throw new InvalidOperationException("未知格式"),
        };
    }
}
