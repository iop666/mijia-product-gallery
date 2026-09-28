using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration.Attributes;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Rules;
using SkiaSharp;

namespace MijiaProductGallery.SeedPackTool;

/// <summary>构建参数。</summary>
public sealed record SeedPackBuildOptions
{
    /// <summary>mijia-product-icons 仓库检出根目录（只读）。</summary>
    public required string IconsDirectory { get; init; }

    /// <summary>aux 保留名图片补充包解压目录（可选；仓库目录树不含 aux.* 文件）。</summary>
    public string? AuxDirectory { get; init; }

    public required string OutputDirectory { get; init; }

    /// <summary>快照日期（yyyy-MM-dd，随仓库 README 声明）。</summary>
    public required string SnapshotDate { get; init; }

    public required string Source { get; init; }
}

/// <summary>构建结果统计。</summary>
public sealed record SeedPackBuildResult
{
    public required string PackagePath { get; init; }

    public required string SnapshotDate { get; init; }

    public required int ProductCount { get; init; }

    public required int WithImage { get; init; }

    public required int MissingImage { get; init; }

    public required int Delisted { get; init; }

    public required int OldImageCount { get; init; }

    public required int ImageFileCount { get; init; }

    public required long PackageBytes { get; init; }

    public required TimeSpan Elapsed { get; init; }
}

/// <summary>
/// seedpack-v1 打包器：读取 mijia-product-icons 检出（总清单 CSV + 扁平图树 + 可选 aux 补充目录），
/// 逐图计算 SHA-256 与尺寸，产出 manifest.json + products.jsonl + images/ 的 zip 包。
/// 对仓库只读。
/// </summary>
public static partial class SeedPackBuilder
{
    private const string DelistedRemark = "官网已移除该型号";

    private static readonly Regex SnapshotDatePattern = DatePattern();

    /// <summary>构建种子包；输出 OutputDirectory\seed-&lt;快照日期&gt;.zip。</summary>
    public static async Task<SeedPackBuildResult> BuildAsync(
        SeedPackBuildOptions options,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (!SnapshotDatePattern.IsMatch(options.SnapshotDate))
        {
            throw new ArgumentException($"快照日期不合法：{options.SnapshotDate}（应为 yyyy-MM-dd）");
        }

        if (!Directory.Exists(options.IconsDirectory))
        {
            throw new DirectoryNotFoundException($"仓库目录不存在：{options.IconsDirectory}");
        }

        var started = Stopwatch.StartNew();
        var allImagesDirectory = Path.Combine(options.IconsDirectory, "全部示例样图");
        if (!Directory.Exists(allImagesDirectory))
        {
            throw new DirectoryNotFoundException($"扁平图树不存在：{allImagesDirectory}");
        }

        var searchDirectories = new List<string> { allImagesDirectory };
        if (options.AuxDirectory is { } aux && Directory.Exists(aux))
        {
            searchDirectories.Add(aux);
        }

        log?.Report("读取总清单…");
        var rows = ReadManifestRows(Path.Combine(options.IconsDirectory, "清单", "00_总清单.csv"));
        var stagingDirectory = Path.Combine(Path.GetTempPath(), "seedpack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        var stagingImages = Path.Combine(stagingDirectory, "images");
        Directory.CreateDirectory(stagingImages);

        try
        {
            var productEntries = new List<ProductLine>();
            var imageFiles = new List<(string RealName, string SourcePath)>();
            var withImage = 0;
            var missing = 0;
            var delisted = 0;
            var oldImageCount = 0;

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var isDelisted = string.Equals(row.Remark, DelistedRemark, StringComparison.Ordinal);
                if (isDelisted)
                {
                    delisted++;
                }

                var entry = new ProductLine
                {
                    Model = row.Model,
                    Name = row.Name,
                    Brand = row.Brand,
                    Category = row.Category,
                    IsAvailable = !isDelisted,
                    CreateTime = 0,
                    UpdateTime = 0,
                };

                if (!string.IsNullOrEmpty(row.ImageFileName))
                {
                    var source = FindImageFile(searchDirectories, row.ImageFileName);
                    if (source is null)
                    {
                        // 官方登记有图但本包缺图（如 aux 保留名图片未随检出分发）。
                        entry.ImageFileName = row.ImageFileName;
                        entry.ImageMissing = true;
                        missing++;
                        log?.Report($"缺图（官方登记）：{row.ImageFileName}");
                    }
                    else
                    {
                        withImage++;
                        var sha = await ComputeSha256Async(source, cancellationToken);
                        var (width, height) = GetDimensions(source, row.ImageFileName, log);
                        var format = GetFormat(source, row.ImageFileName);
                        entry.ImageFileName = row.ImageFileName;
                        entry.Sha256 = sha;
                        entry.Format = format.ToString().ToLowerInvariant();
                        entry.Width = width;
                        entry.Height = height;
                        entry.FileLength = new FileInfo(source).Length;
                        imageFiles.Add((row.ImageFileName, source));

                        var oldFiles = FindOldImages(searchDirectories, row.ImageFileName);
                        foreach (var oldFile in oldFiles)
                        {
                            var oldName = Path.GetFileName(oldFile);
                            entry.OldImages.Add(new OldImageLine(oldName, await ComputeSha256Async(oldFile, cancellationToken)));
                            imageFiles.Add((oldName, oldFile));
                            oldImageCount++;
                        }
                    }
                }

                productEntries.Add(entry);
            }

            log?.Report($"写入 products.jsonl（{productEntries.Count} 行）…");
            var productsPath = Path.Combine(stagingDirectory, "products.jsonl");
            await WriteProductsJsonlAsync(productsPath, productEntries, cancellationToken);
            var productsSha = await ComputeSha256Async(productsPath, cancellationToken);

            var manifest = new
            {
                schemaVersion = 1,
                snapshotDate = options.SnapshotDate,
                source = options.Source,
                productCount = productEntries.Count,
                imageCount = withImage,
                imageFileCount = imageFiles.Count,
                productsSha256 = productsSha,
                generator = "SeedPackTool v1",
            };
            var manifestPath = Path.Combine(stagingDirectory, "manifest.json");
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                }),
                new UTF8Encoding(false),
                cancellationToken);

            Directory.CreateDirectory(options.OutputDirectory);
            var packagePath = Path.Combine(options.OutputDirectory, $"seed-{options.SnapshotDate}.zip");
            log?.Report($"压缩种子包（{imageFiles.Count} 个图片文件）…");
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }

            await using (var packageStream = File.Create(packagePath))
            await using (var zip = new ZipArchive(packageStream, ZipArchiveMode.Create))
            {
                await AddFileEntryAsync(zip, manifestPath, "manifest.json", cancellationToken);
                await AddFileEntryAsync(zip, productsPath, "products.jsonl", cancellationToken);
                foreach (var (realName, source) in imageFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await AddFileEntryAsync(zip, source, $"images/{realName}", cancellationToken);
                }
            }

            return new SeedPackBuildResult
            {
                PackagePath = packagePath,
                SnapshotDate = options.SnapshotDate,
                ProductCount = productEntries.Count,
                WithImage = withImage,
                MissingImage = missing,
                Delisted = delisted,
                OldImageCount = oldImageCount,
                ImageFileCount = imageFiles.Count,
                PackageBytes = new FileInfo(packagePath).Length,
                Elapsed = started.Elapsed,
            };
        }
        finally
        {
            try
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string? FindImageFile(IReadOnlyList<string> directories, string fileName)
    {
        return directories
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(File.Exists);
    }

    private static string[] FindOldImages(IReadOnlyList<string> directories, string imageFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(imageFileName);
        var extension = Path.GetExtension(imageFileName);
        return directories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.GetFiles(directory, baseName + ".old*" + extension))
            .ToArray();
    }

    private static async Task WriteProductsJsonlAsync(string path, IReadOnlyList<ProductLine> entries, CancellationToken cancellationToken)
    {
        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        await using var stream = File.Create(path);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        foreach (var entry in entries)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(entry, serializerOptions).AsMemory(), cancellationToken);
        }
    }

    private static async Task AddFileEntryAsync(ZipArchive zip, string sourcePath, string entryName, CancellationToken cancellationToken)
    {
        await using var source = File.OpenRead(sourcePath);
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var target = entry.Open();
        await source.CopyToAsync(target, cancellationToken);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static (int Width, int Height) GetDimensions(string path, string fileName, IProgress<string>? log)
    {
        using var bitmap = SKBitmap.Decode(path);
        if (bitmap is null)
        {
            throw new InvalidDataException($"图片无法解码（仓库数据异常）：{fileName}");
        }

        return (bitmap.Info.Width, bitmap.Info.Height);
    }

    private static ImageFormat GetFormat(string path, string fileName)
    {
        var header = new byte[16];
        using (var stream = File.OpenRead(path))
        {
            var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            var format = ImageHeaderRules.DetectFormat(header.AsSpan(0, read));
            if (format == ImageFormat.Unknown)
            {
                throw new InvalidDataException($"图片格式无法识别（仓库数据异常）：{fileName}");
            }

            return format;
        }
    }

    private static List<ManifestRow> ReadManifestRows(string csvPath)
    {
        using var reader = new StreamReader(csvPath, Encoding.UTF8);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return [.. csv.GetRecords<ManifestRow>()];
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex DatePattern();

    private sealed class ManifestRow
    {
        [Name("大类")]
        public string Category { get; set; } = string.Empty;

        [Name("品牌")]
        public string Brand { get; set; } = string.Empty;

        [Name("产品名称")]
        public string Name { get; set; } = string.Empty;

        [Name("型号")]
        public string Model { get; set; } = string.Empty;

        [Name("图片文件名")]
        public string ImageFileName { get; set; } = string.Empty;

        [Name("备注")]
        public string Remark { get; set; } = string.Empty;
    }

    internal sealed class ProductLine
    {
        public string Model { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Brand { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public bool IsAvailable { get; set; } = true;

        public string? ImageFileName { get; set; }

        public string? Sha256 { get; set; }

        public string? Format { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public long FileLength { get; set; }

        public long? CreateTime { get; set; }

        public long? UpdateTime { get; set; }

        public bool ImageMissing { get; set; }

        public List<OldImageLine> OldImages { get; set; } = [];
    }

    internal sealed record OldImageLine(string FileName, string Sha256);
}
