namespace MijiaProductGallery.Core.Rules;

/// <summary>
/// 图片文件名规则：Windows 保留设备名安全映射与 .old 链式命名。
/// 纯函数，不含任何环境依赖。
/// </summary>
public static class FileNameRules
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AUX", "CON", "PRN", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>取文件名首个小数点之前的主名（型号）。</summary>
    public static string GetModelStem(string fileName)
    {
        var dotIndex = fileName.IndexOf('.');
        return dotIndex < 0 ? fileName : fileName[..dotIndex];
    }

    /// <summary>判断型号（或文件名）主名是否为 Windows 保留设备名（如 aux.aircondition.hc1）。</summary>
    public static bool IsReservedDeviceModel(string modelOrFileName)
    {
        return ReservedDeviceNames.Contains(GetModelStem(modelOrFileName));
    }

    /// <summary>
    /// 原始文件名 → 磁盘安全文件名。保留设备名主名后加下划线：
    /// aux.aircondition.hc1.png → aux_.aircondition.hc1.png；其余原样返回。
    /// </summary>
    public static string ToDiskFileName(string imageFileName)
    {
        var dotIndex = imageFileName.IndexOf('.');
        if (dotIndex < 0)
        {
            return imageFileName;
        }

        var stem = imageFileName[..dotIndex];
        return ReservedDeviceNames.Contains(stem)
            ? $"{stem}_.{imageFileName[(dotIndex + 1)..]}"
            : imageFileName;
    }

    /// <summary>磁盘安全文件名 → 原始文件名（ToDiskFileName 的逆运算，非安全名原样返回）。</summary>
    public static string FromDiskFileName(string diskFileName)
    {
        var dotIndex = diskFileName.IndexOf('.');
        if (dotIndex < 0)
        {
            return diskFileName;
        }

        var stem = diskFileName[..dotIndex];
        return stem.Length > 1
            && stem.EndsWith('_')
            && ReservedDeviceNames.Contains(stem[..^1])
                ? $"{stem[..^1]}.{diskFileName[(dotIndex + 1)..]}"
                : diskFileName;
    }

    /// <summary>判断文件名是否为 .old 系列旧图（如 xxx.old.png、xxx.old1.png）。</summary>
    public static bool IsOldImageFileName(string fileName)
    {
        return TryGetOldGeneration(fileName, out _);
    }

    /// <summary>解析 .old 系列旧图的代数：xxx.old.png 为 0，xxx.old1.png 为 1，以此类推。</summary>
    public static bool TryGetOldGeneration(string fileName, out int generation)
    {
        generation = -1;
        var dotIndex = fileName.LastIndexOf('.');
        if (dotIndex <= 0)
        {
            return false;
        }

        var withoutExtension = fileName[..dotIndex];
        var markerIndex = withoutExtension.LastIndexOf(".old", StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var marker = withoutExtension[(markerIndex + 4)..];
        if (marker.Length == 0)
        {
            generation = 0;
            return true;
        }

        return marker[0] is >= '0' and <= '9'
            && int.TryParse(marker, out generation)
            && generation > 0;
    }

    /// <summary>计算指定代数的旧图文件名：代数 0 → xxx.old.png，代数 1 → xxx.old1.png，以此类推。</summary>
    public static string GetOldImageFileName(string imageFileName, int generation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        var dotIndex = imageFileName.LastIndexOf('.');
        if (dotIndex < 0)
        {
            return generation == 0 ? $"{imageFileName}.old" : $"{imageFileName}.old{generation}";
        }

        var baseName = imageFileName[..dotIndex];
        var extension = imageFileName[dotIndex..];
        return generation == 0
            ? $"{baseName}.old{extension}"
            : $"{baseName}.old{generation}{extension}";
    }

    /// <summary>根据磁盘上已有的 .old 旧图计算下一个可用代数（无旧图为 0，已有 .old 与 .old1 时为 2）。</summary>
    public static int GetNextOldGeneration(IEnumerable<string> existingOldImageFiles)
    {
        var maxGeneration = -1;
        foreach (var fileName in existingOldImageFiles)
        {
            if (TryGetOldGeneration(fileName, out var generation) && generation > maxGeneration)
            {
                maxGeneration = generation;
            }
        }

        return maxGeneration + 1;
    }
}
