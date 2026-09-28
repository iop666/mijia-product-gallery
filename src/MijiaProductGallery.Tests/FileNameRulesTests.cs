using MijiaProductGallery.Core.Rules;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>文件名规则测试：Windows 保留设备名映射与 .old 链式命名。</summary>
public sealed class FileNameRulesTests
{
    [Theory]
    [InlineData("aux.aircondition.hc1", true)]
    [InlineData("aux.aircondition.v1.png", true)]
    [InlineData("CON.test", true)]
    [InlineData("com1.device", true)]
    [InlineData("LPT3.printer", true)]
    [InlineData("zhimi.heater.za1", false)]
    [InlineData("giot.bhf_light.auxbh", false)]
    public void IsReservedDeviceModel_DetectsByStem(string model, bool expected)
    {
        Assert.Equal(expected, FileNameRules.IsReservedDeviceModel(model));
    }

    [Fact]
    public void ToDiskFileName_MapsReservedPrefix()
    {
        Assert.Equal(
            "aux_.aircondition.hc1.png",
            FileNameRules.ToDiskFileName("aux.aircondition.hc1.png"));
        Assert.Equal(
            "aux.aircondition.hc1.png",
            FileNameRules.FromDiskFileName("aux_.aircondition.hc1.png"));
    }

    [Fact]
    public void DiskFileNameRoundTrip_LeavesNormalNamesUntouched()
    {
        const string name = "zhimi.heater.za1.png";
        Assert.Equal(name, FileNameRules.ToDiskFileName(name));
        Assert.Equal(name, FileNameRules.FromDiskFileName(name));
    }

    [Theory]
    [InlineData(0, "chuangmi.camera.029a02.old.png")]
    [InlineData(1, "chuangmi.camera.029a02.old1.png")]
    [InlineData(2, "chuangmi.camera.029a02.old2.png")]
    public void GetOldImageFileName_BuildsChain(int generation, string expected)
    {
        Assert.Equal(
            expected,
            FileNameRules.GetOldImageFileName("chuangmi.camera.029a02.png", generation));
    }

    [Theory]
    [InlineData("chuangmi.camera.029a02.old.png", 0)]
    [InlineData("chuangmi.camera.029a02.old1.png", 1)]
    [InlineData("chuangmi.camera.029a02.old2.png", 2)]
    [InlineData("zhimi.heater.za1.png", -1)]
    [InlineData("mi.old.fan.png", -1)]
    public void TryGetOldGeneration_ParsesMarker(string fileName, int expectedGeneration)
    {
        var result = FileNameRules.TryGetOldGeneration(fileName, out var generation);
        if (expectedGeneration < 0)
        {
            Assert.False(result);
        }
        else
        {
            Assert.True(result);
            Assert.Equal(expectedGeneration, generation);
        }
    }

    [Fact]
    public void GetNextOldGeneration_TakesHighestExisting()
    {
        Assert.Equal(0, FileNameRules.GetNextOldGeneration([]));
        Assert.Equal(
            1,
            FileNameRules.GetNextOldGeneration(["chuangmi.camera.029a02.old.png"]));
        Assert.Equal(
            2,
            FileNameRules.GetNextOldGeneration([
                "chuangmi.camera.029a02.old1.png",
                "chuangmi.camera.029a02.old.png",
            ]));
    }
}
