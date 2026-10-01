namespace MijiaProductGallery.Core;

/// <summary>按已完成计数与已用时长估算剩余时间（同步进度与首启加载窗口共用）。</summary>
public static class SyncEta
{
    /// <summary>
    /// 估算剩余时间。样本不足（无总量、未完成任何计数、用时过短）或已完成时返回 null；
    /// 估算结果超过 6 小时视为不可信，同样返回 null。
    /// </summary>
    public static TimeSpan? Estimate(int done, int total, double elapsedSeconds)
    {
        if (total <= 0 || done <= 0 || done >= total || elapsedSeconds < 1)
        {
            return null;
        }

        var rate = done / elapsedSeconds;
        if (rate <= 0)
        {
            return null;
        }

        var seconds = (total - done) / rate;
        return seconds > 6 * 3600 ? null : TimeSpan.FromSeconds(seconds);
    }

    /// <summary>中文格式："1 时 05 分" / "3 分 12 秒" / "42 秒"。</summary>
    public static string Format(TimeSpan remaining)
    {
        return remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours} 时 {remaining.Minutes:00} 分"
            : remaining.TotalMinutes >= 1
                ? $"{remaining.Minutes} 分 {remaining.Seconds:00} 秒"
                : $"{remaining.Seconds} 秒";
    }

    /// <summary>时钟格式："mm:ss"（超 1 小时为 "h:mm:ss"）。</summary>
    public static string FormatClock(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";
    }
}
