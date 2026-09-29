using System.IO.Compression;
using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.ViewModels;

/// <summary>
/// 收藏夹 ZIP 导出实现：纯文件操作（不依赖数据库），逐条流式写入 ZipArchive，
/// 支持进度上报与取消；取消时删除半成品 ZIP。文件名安全化（非法字符/空名/超长）
/// 与重名去重（"名称 (n).ext"）保证不覆盖任何已导出文件。
/// </summary>
public sealed class CollectionExportService : ICollectionExportService
{
    private const int MaxBaseNameLength = 120;

    public async Task<CollectionExportResult> ExportAsync(
        CollectionExportRequest request,
        IProgress<CollectionExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var exported = 0;
        var failed = new List<string>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = request.Items.Count;
        var completed = 0;

        try
        {
            var directory = Path.GetDirectoryName(request.ZipFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var fileStream = new FileStream(
                request.ZipFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);

            foreach (var item in request.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (string.IsNullOrWhiteSpace(item.ImagePath))
                    {
                        throw new FileNotFoundException("产品无图片文件");
                    }

                    // 先成功打开源文件再创建条目：失败不留空 ZIP 条目。
                    await using var source = new FileStream(
                        item.ImagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
                    var entryName = NextEntryName(item, request.NameByModel, usedNames);
                    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await source.CopyToAsync(entryStream, cancellationToken);
                    if (source.Length <= 0)
                    {
                        throw new IOException("图片文件为空");
                    }

                    usedNames.Add(entryName);
                    exported++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failed.Add($"{item.Name}（{item.Model}）：{exception.Message}");
                }

                completed++;
                progress?.Report(new CollectionExportProgress(completed, total));
            }

            return new CollectionExportResult { Cancelled = false, ExportedCount = exported, FailedItems = failed };
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (File.Exists(request.ZipFilePath))
                {
                    File.Delete(request.ZipFilePath);
                }
            }
            catch
            {
                // 删除半成品失败不掩盖取消结果本身。
            }

            return new CollectionExportResult { Cancelled = true, ExportedCount = exported, FailedItems = [] };
        }
    }

    /// <summary>生成唯一条目名：Model 或设备名称 + 原扩展名；冲突时追加 " (n)"。</summary>
    private static string NextEntryName(CollectionExportItem item, bool nameByModel, HashSet<string> usedNames)
    {
        var extension = Path.GetExtension(item.ImagePath!);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".png";
        }

        var baseName = Sanitize(nameByModel ? item.Model : item.Name);
        if (baseName.Length == 0)
        {
            // 设备名称为空时回落 Model。
            baseName = Sanitize(item.Model);
        }
        var candidate = baseName + extension;
        var serial = 1;
        while (usedNames.Contains(candidate))
        {
            candidate = $"{baseName} ({serial++}){extension}";
        }

        return candidate;
    }

    /// <summary>文件名安全化：非法字符/控制字符替换为下划线，去除结尾点与空格，空名回落 Model，超长截断。</summary>
    private static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var chars = raw.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i]) || "<>:\"/\\|?*".Contains(chars[i]))
            {
                chars[i] = '_';
            }
        }

        var name = new string(chars).TrimEnd('.', ' ');
        if (name.Length > MaxBaseNameLength)
        {
            name = name[..MaxBaseNameLength].TrimEnd('.', ' ');
        }

        return name.Length == 0 ? "未命名产品" : name;
    }
}
