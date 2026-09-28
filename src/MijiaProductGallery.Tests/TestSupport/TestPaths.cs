namespace MijiaProductGallery.Tests.TestSupport;

/// <summary>测试夹具的固定路径（相对测试输出目录，不含本机路径）。</summary>
public static class TestPaths
{
    public static string FixturesRoot { get; } =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");
}
