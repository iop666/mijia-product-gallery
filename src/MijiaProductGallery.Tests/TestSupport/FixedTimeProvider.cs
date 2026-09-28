using System.Globalization;

namespace MijiaProductGallery.Tests.TestSupport;

/// <summary>固定时钟（测试时间边界用）。</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }
}
