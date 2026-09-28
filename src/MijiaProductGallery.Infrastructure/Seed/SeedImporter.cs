using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Rules;
using MijiaProductGallery.Infrastructure.Database;

namespace MijiaProductGallery.Infrastructure.Seed;

/// <summary>种子包校验失败：manifest 结构/版本、计数、产品清单字段或图片内容不符。</summary>
public sealed class SeedPackageException(string message) : InvalidOperationException(message);

/// <summary>
/// seedpack-v1 导入器：manifest 与 products.jsonl 校验 → 逐文件流式 SHA 校验并原子复制
/// （已存在且 SHA 一致即跳过，损坏即修复）→ 单事务批量写入官方列 → 快照日期收口。
/// 只写 Products 官方列，不触碰任何用户数据；任何失败不破坏既有数据库。
/// </summary>
public sealed class SeedImporter(GalleryDbContext dbContext, DatabasePaths paths) : ISeedImporter
{
    private const string ManifestEntryName = "manifest.json";
    private const string ProductsEntryName = "products.jsonl";
    private const string ImagesEntryPrefix = "images/";
    private const int DatabaseBatchSize = 500;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions EntryJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public async Task<SeedImportResult> ImportAsync(
        string packagePath,
        IProgress<SeedImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
        {
            throw new SeedPackageException($"种子包不存在：{packagePath}");
        }

        paths.EnsureDirectories();
        await using var packageStream = File.OpenRead(packagePath);
        using var zip = new ZipArchive(packageStream, ZipArchiveMode.Read);

        var manifest = await ReadManifestAsync(zip, cancellationToken);
        var entries = await ReadProductEntriesAsync(zip, manifest, cancellationToken);
        var expectedFiles = CollectExpectedFiles(entries);
        VerifyImageFileSet(zip, expectedFiles, manifest);

        await RejectOlderSnapshotAsync(manifest, cancellationToken);

        var copyStats = await CopyImagesAsync(zip, expectedFiles, manifest, progress, cancellationToken);

        var (added, updated) = await WriteDatabaseAsync(entries, manifest, progress, cancellationToken);
        await FinalizeSnapshotDateAsync(manifest, cancellationToken);

        return new SeedImportResult
        {
            SnapshotDate = manifest.SnapshotDate,
            ProductsTotal = entries.Count,
            ProductsAdded = added,
            ProductsUpdated = updated,
            ImageFilesTotal = expectedFiles.Count,
            ImagesSkipped = copyStats.Skipped,
            ImagesCopied = copyStats.Copied,
            ImagesRepaired = copyStats.Repaired,
        };
    }

    private async Task<SeedManifest> ReadManifestAsync(ZipArchive zip, CancellationToken cancellationToken)
    {
        var entry = zip.GetEntry(ManifestEntryName)
            ?? throw new SeedPackageException("种子包缺少 manifest.json");
        using var reader = new StreamReader(entry.Open());
        var manifest = await JsonSerializer.DeserializeAsync<SeedManifest>(
            reader.BaseStream, ManifestJsonOptions, cancellationToken)
            ?? throw new SeedPackageException("manifest.json 无法解析");
        if (manifest.SchemaVersion != 1)
        {
            throw new SeedPackageException($"不支持的种子包版本：{manifest.SchemaVersion}（当前支持 1）");
        }

        if (string.IsNullOrWhiteSpace(manifest.SnapshotDate)
            || !System.Text.RegularExpressions.Regex.IsMatch(manifest.SnapshotDate, @"^\d{4}-\d{2}-\d{2}$"))
        {
            throw new SeedPackageException($"manifest 快照日期不合法：{manifest.SnapshotDate}");
        }

        return manifest;
    }

    private async Task<List<SeedEntry>> ReadProductEntriesAsync(ZipArchive zip, SeedManifest manifest, CancellationToken cancellationToken)
    {
        var entry = zip.GetEntry(ProductsEntryName)
            ?? throw new SeedPackageException("种子包缺少 products.jsonl");
        // zip 条目流不可 Seek：一次性缓冲后校验与解析（清单文件仅数 MB）。
        using var buffered = new MemoryStream();
        await using (var productStream = entry.Open())
        {
            await productStream.CopyToAsync(buffered, cancellationToken);
        }

        var jsonlBytes = buffered.ToArray();
        var productsSha = Convert.ToHexString(SHA256.HashData(jsonlBytes)).ToLowerInvariant();
        if (!string.Equals(productsSha, manifest.ProductsSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new SeedPackageException("products.jsonl 与 manifest 校验值不符（包内容损坏或不匹配）");
        }

        var entries = new List<SeedEntry>(manifest.ProductCount);
        using var reader = new StreamReader(new MemoryStream(jsonlBytes), Encoding.UTF8);
        var lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            SeedEntry parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<SeedEntry>(line, EntryJsonOptions)
                    ?? throw new SeedPackageException($"products.jsonl 第 {lineNumber} 行无法解析");
            }
            catch (JsonException exception)
            {
                throw new SeedPackageException($"products.jsonl 第 {lineNumber} 行解析失败：{exception.Message}");
            }

            if (string.IsNullOrWhiteSpace(parsed.Model) || string.IsNullOrWhiteSpace(parsed.Name)
                || string.IsNullOrWhiteSpace(parsed.Brand) || string.IsNullOrWhiteSpace(parsed.Category))
            {
                throw new SeedPackageException($"products.jsonl 第 {lineNumber} 行缺少必填字段（model/name/brand/category）");
            }

            entries.Add(parsed);
        }

        if (entries.Count != manifest.ProductCount)
        {
            throw new SeedPackageException(
                $"manifest 产品数不符：声明 {manifest.ProductCount}，实际 {entries.Count}");
        }

        return entries;
    }

    private static Dictionary<string, string> CollectExpectedFiles(List<SeedEntry> entries)
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.ImageFileName is not null && !entry.ImageMissing)
            {
                if (string.IsNullOrWhiteSpace(entry.Sha256))
                {
                    throw new SeedPackageException($"产品 {entry.Model} 登记了图片但缺少 SHA-256");
                }

                expected[entry.ImageFileName] = entry.Sha256;
            }

            foreach (var old in entry.OldImages)
            {
                expected[old.FileName] = old.Sha256;
            }
        }

        return expected;
    }

    private static void VerifyImageFileSet(ZipArchive zip, Dictionary<string, string> expectedFiles, SeedManifest manifest)
    {
        var zipImageNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.StartsWith(ImagesEntryPrefix, StringComparison.Ordinal)
                && entry.FullName.Length > ImagesEntryPrefix.Length)
            {
                zipImageNames.Add(entry.FullName[ImagesEntryPrefix.Length..]);
            }
        }

        var missing = expectedFiles.Keys.Where(name => !zipImageNames.Contains(name)).ToList();
        var extra = zipImageNames.Where(name => !expectedFiles.ContainsKey(name)).ToList();
        if (missing.Count > 0)
        {
            throw new SeedPackageException($"种子包缺少登记图片：{string.Join(", ", missing.Take(5))}（共 {missing.Count}）");
        }

        if (extra.Count > 0)
        {
            throw new SeedPackageException($"种子包含未登记图片：{string.Join(", ", extra.Take(5))}（共 {extra.Count}）");
        }

        if (zipImageNames.Count != manifest.ImageFileCount)
        {
            throw new SeedPackageException(
                $"manifest 图片文件数不符：声明 {manifest.ImageFileCount}，实际 {zipImageNames.Count}");
        }
    }

    private async Task RejectOlderSnapshotAsync(SeedManifest manifest, CancellationToken cancellationToken)
    {
        var hasProducts = await dbContext.Products.AnyAsync(cancellationToken);
        if (!hasProducts)
        {
            return;
        }

        var state = await dbContext.SyncState.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
        var localSnapshot = state?.SnapshotDate;
        if (!string.IsNullOrWhiteSpace(localSnapshot)
            && string.CompareOrdinal(localSnapshot, manifest.SnapshotDate) > 0)
        {
            throw new SeedPackageException(
                $"本地数据快照（{localSnapshot}）新于种子包（{manifest.SnapshotDate}），拒绝降级导入");
        }
    }

    private async Task<(int Copied, int Skipped, int Repaired)> CopyImagesAsync(
        ZipArchive zip,
        Dictionary<string, string> expectedFiles,
        SeedManifest manifest,
        IProgress<SeedImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var copied = 0;
        var skipped = 0;
        var repaired = 0;
        var imageEntries = zip.Entries
            .Where(entry => entry.FullName.StartsWith(ImagesEntryPrefix, StringComparison.Ordinal)
                && entry.FullName.Length > ImagesEntryPrefix.Length)
            .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
            .ToList();

        progress?.Report(new SeedImportProgress
        {
            Stage = SeedImportStage.CopyingImages,
            ImagesTotal = imageEntries.Count,
        });

        foreach (var entry in imageEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var realName = entry.FullName[ImagesEntryPrefix.Length..];
            var tempPath = Path.Combine(paths.ImagesDirectory, $".seed-{Guid.NewGuid():N}");
            try
            {
                await using (var source = entry.Open())
                await using (var temp = File.Create(tempPath))
                {
                    await source.CopyToAsync(temp, cancellationToken);
                }

                var actualSha = await ComputeFileSha256Async(tempPath, cancellationToken);
                if (!string.Equals(actualSha, expectedFiles[realName], StringComparison.OrdinalIgnoreCase))
                {
                    throw new SeedPackageException($"图片内容与清单 SHA 不符：{realName}");
                }

                var target = Path.Combine(
                    paths.ImagesDirectory,
                    FileNameRules.ToDiskFileName(realName));
                if (File.Exists(target))
                {
                    var existingSha = await ComputeFileSha256Async(target, cancellationToken);
                    if (string.Equals(existingSha, actualSha, StringComparison.OrdinalIgnoreCase))
                    {
                        skipped++;
                    }
                    else
                    {
                        // 磁盘现图损坏（SHA 不一致）：以校验通过的种子图修复。
                        File.Move(tempPath, target, overwrite: true);
                        repaired++;
                        copied++;
                    }
                }
                else
                {
                    File.Move(tempPath, target);
                    copied++;
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            progress?.Report(new SeedImportProgress
            {
                Stage = SeedImportStage.CopyingImages,
                ImagesTotal = imageEntries.Count,
                ImagesDone = copied + skipped,
                CurrentFile = realName,
            });
        }

        return (copied, skipped, repaired);
    }

    private async Task<(int Added, int Updated)> WriteDatabaseAsync(
        List<SeedEntry> entries,
        SeedManifest manifest,
        IProgress<SeedImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new SeedImportProgress
        {
            Stage = SeedImportStage.WritingDatabase,
            ProductsTotal = entries.Count,
        });

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var added = 0;
        var updated = 0;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var existingRows = await dbContext.Products.ToDictionaryAsync(p => p.Model, cancellationToken);
        var batch = new List<Product>(DatabaseBatchSize);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (existingRows.TryGetValue(entry.Model, out var row))
            {
                AssignOfficialFields(row, entry, now);
                updated++;
                batch.Add(row);
            }
            else
            {
                var product = new Product
                {
                    Model = entry.Model,
                    Name = entry.Name,
                    Brand = entry.Brand,
                    Category = entry.Category,
                    FirstSeenUnix = now,
                    RandomKey = Random.Shared.NextInt64(),
                };
                AssignOfficialFields(product, entry, now);
                existingRows[entry.Model] = product;
                added++;
                batch.Add(product);
            }

            if (batch.Count >= DatabaseBatchSize)
            {
                dbContext.Products.UpdateRange(batch.Where(p => p.Id != 0));
                dbContext.Products.AddRange(batch.Where(p => p.Id == 0));
                await dbContext.SaveChangesAsync(cancellationToken);
                batch.Clear();
                progress?.Report(new SeedImportProgress
                {
                    Stage = SeedImportStage.WritingDatabase,
                    ProductsTotal = entries.Count,
                    ProductsDone = added + updated,
                });
            }
        }

        if (batch.Count > 0)
        {
            dbContext.Products.UpdateRange(batch.Where(p => p.Id != 0));
            dbContext.Products.AddRange(batch.Where(p => p.Id == 0));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (added, updated);
    }

    /// <summary>只写官方列；FirstSeenUnix 仅在新行创建时设定，既有行保留本地事实。</summary>
    private static void AssignOfficialFields(Product product, SeedEntry entry, long now)
    {
        product.Name = entry.Name;
        product.Brand = entry.Brand;
        product.Category = entry.Category;
        product.SubCategory = null;
        product.ImageFileName = entry.ImageFileName;
        product.ImagePath = entry.Sha256 is null || entry.ImageFileName is null
            ? null
            : $"images/{FileNameRules.ToDiskFileName(entry.ImageFileName)}";
        product.ImageUrl = null;
        product.ImageFormat = entry.Format is null
            ? ImageFormat.Unknown
            : Enum.TryParse<ImageFormat>(entry.Format, ignoreCase: true, out var format) ? format : ImageFormat.Unknown;
        product.ImageWidth = entry.Width;
        product.ImageHeight = entry.Height;
        product.FileSize = entry.FileLength;
        product.Sha256 = entry.Sha256;
        product.IsAvailable = entry.IsAvailable;
        product.CreateTimeUnix = entry.CreateTime;
        product.UpdateTimeUnix = entry.UpdateTime;
        product.LastSeenUnix = now;
    }

    private async Task FinalizeSnapshotDateAsync(SeedManifest manifest, CancellationToken cancellationToken)
    {
        var state = await dbContext.SyncState.FirstOrDefaultAsync(s => s.Id == 1, cancellationToken);
        if (state is null)
        {
            dbContext.SyncState.Add(new SyncState
            {
                Id = 1,
                SnapshotDate = manifest.SnapshotDate,
            });
        }
        else
        {
            dbContext.SyncState.Remove(state);
            dbContext.SyncState.Add(state with
            {
                SnapshotDate = manifest.SnapshotDate,
                Status = SyncStatus.Idle,
                Stage = SyncStage.NotStarted,
                ErrorMessage = null,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    internal sealed class SeedManifest
    {
        public int SchemaVersion { get; set; }

        public string SnapshotDate { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public int ProductCount { get; set; }

        /// <summary>现役图数量（有图型号数）。</summary>
        public int ImageCount { get; set; }

        /// <summary>images\ 目录文件总数（含 .old 历史图）。</summary>
        public int ImageFileCount { get; set; }

        public string ProductsSha256 { get; set; } = string.Empty;

        public string Generator { get; set; } = string.Empty;
    }

    internal sealed class SeedEntry
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

        /// <summary>官方登记有图但本包缺图（如 aux 保留名未随包分发）。</summary>
        public bool ImageMissing { get; set; }

        public List<SeedOldImage> OldImages { get; set; } = [];
    }

    internal sealed record SeedOldImage(
        [property: JsonPropertyName("fileName")] string FileName,
        [property: JsonPropertyName("sha256")] string Sha256);
}
