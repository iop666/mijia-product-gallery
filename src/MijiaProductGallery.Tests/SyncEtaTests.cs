using MijiaProductGallery.Core;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>剩余时间估算：速率线性外推，异常输入返回 null，格式化分档。</summary>
public sealed class SyncEtaTests
{
    [Fact]
    public void Estimate_ComputesRemainingFromRate()
    {
        // 10 秒完成 100 项 → 剩余 100 项需 10 秒。
        var eta = SyncEta.Estimate(100, 200, 10);
        Assert.NotNull(eta);
        Assert.Equal(10, eta.Value.TotalSeconds, precision: 0);
    }

    [Theory]
    [InlineData(0, 100, 10)] // 尚未完成任何项：样本不足
    [InlineData(50, 0, 10)] // 无总量
    [InlineData(50, 50, 10)] // 已完成
    [InlineData(200, 100, 10)] // 计数异常
    [InlineData(50, 100, 0.5)] // 用时过短
    public void Estimate_ReturnsNull_ForInvalidInputs(int done, int total, double elapsedSeconds)
    {
        Assert.Null(SyncEta.Estimate(done, total, elapsedSeconds));
    }

    [Fact]
    public void Estimate_ReturnsNull_WhenProjectionExceedsSixHours()
    {
        // 1 项/秒 → 剩余 30,000 项 ≈ 8.3 小时，不可信。
        Assert.Null(SyncEta.Estimate(1, 30_001, 1));
    }

    [Fact]
    public void Format_CoversHoursMinutesSeconds()
    {
        Assert.Equal("2 时 05 分", SyncEta.Format(new TimeSpan(2, 5, 0)));
        Assert.Equal("3 分 12 秒", SyncEta.Format(new TimeSpan(0, 3, 12)));
        Assert.Equal("42 秒", SyncEta.Format(new TimeSpan(0, 0, 42)));
    }

    [Fact]
    public void FormatClock_PadsMinutesSeconds()
    {
        Assert.Equal("02:13", SyncEta.FormatClock(133));
        Assert.Equal("1:02:03", SyncEta.FormatClock(3723));
    }
}
