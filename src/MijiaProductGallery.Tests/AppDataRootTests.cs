using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Infrastructure.Database;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>
/// 数据根派生：默认根为"应用目录\Data"（安装版/便携版统一），全部子目录由唯一根派生；
/// 注入替代根（测试）完整生效。
/// </summary>
public sealed class AppDataRootTests
{
    [Fact]
    public void DefaultRoot_IsAppDirectory_Data()
    {
        var paths = new DatabasePaths();
        var expected = Path.Combine(AppContext.BaseDirectory, "Data");
        Assert.Equal(expected, paths.Root);
        Assert.Equal(expected, ((IAppDataRoot)paths).RootPath);
    }

    [Fact]
    public void OverrideRoot_AllSubDirectoriesDeriveFromIt()
    {
        var paths = new DatabasePaths(@"D:\portable");
        Assert.Equal(@"D:\portable", paths.Root);
        Assert.Equal(@"D:\portable\Database\gallery.db", paths.DatabaseFile);
        Assert.Equal(@"D:\portable\Images", paths.ImagesDirectory);
        Assert.Equal(@"D:\portable\Thumbnails", paths.ThumbnailsDirectory);
        Assert.Equal(@"D:\portable\Backups", paths.BackupsDirectory);
        Assert.Equal(@"D:\portable\Logs", paths.LogsDirectory);
        Assert.Equal(@"D:\portable\Cache", paths.CacheDirectory);
        Assert.Equal(@"D:\portable\State", paths.StateDirectory);
        Assert.Equal(@"D:\portable\Seed", paths.SeedDirectory);
    }

    [Fact]
    public void EnsureDirectories_CreatesAll_SubDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "mpg-root-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new DatabasePaths(root);
            paths.EnsureDirectories();

            Assert.True(Directory.Exists(paths.DatabaseDirectory));
            Assert.True(Directory.Exists(paths.ImagesDirectory));
            Assert.True(Directory.Exists(paths.ThumbnailsDirectory));
            Assert.True(Directory.Exists(paths.BackupsDirectory));
            Assert.True(Directory.Exists(paths.LogsDirectory));
            Assert.True(Directory.Exists(paths.CacheDirectory));
            Assert.True(Directory.Exists(paths.StateDirectory));
            Assert.True(Directory.Exists(paths.SeedDirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
