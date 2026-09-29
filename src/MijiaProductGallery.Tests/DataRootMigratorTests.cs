using MijiaProductGallery.Infrastructure.Database;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>
/// 历史数据根一次性迁移：幂等（已迁移跳过）、整目录搬迁（数据库/图片/缩略图/备份/日志）、
/// 冲突不覆盖、全部成功后清理旧根、部分失败保留旧根、迁移日志落盘。
/// </summary>
public sealed class DataRootMigratorTests : IDisposable
{
    private readonly string sandbox = Path.Combine(
        Path.GetTempPath(), "mpg-migrator-tests", Guid.NewGuid().ToString("N"));

    private string LegacyRoot => Path.Combine(sandbox, "legacy-root");

    public DataRootMigratorTests()
    {
        Directory.CreateDirectory(LegacyRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(sandbox, recursive: true);
        }
        catch
        {
        }
    }

    private string LegacySub(string name) => Path.Combine(LegacyRoot, name);

    /// <summary>构造与真实旧布局一致的legacy目录（小写子目录名 + 数据库文件 + 图片文件）。</summary>
    private void SeedLegacyData()
    {
        Directory.CreateDirectory(LegacySub("database"));
        File.WriteAllText(Path.Combine(LegacySub("database"), "gallery.db"), "legacy-db-content");
        Directory.CreateDirectory(LegacySub("images"));
        File.WriteAllText(Path.Combine(LegacySub("images"), "zhimi.heater.za1.png"), "image-bytes");
        Directory.CreateDirectory(LegacySub("thumbnails"));
        File.WriteAllText(Path.Combine(LegacySub("thumbnails"), "thumb.webp"), "thumb");
        Directory.CreateDirectory(LegacySub("backups"));
        File.WriteAllText(Path.Combine(LegacySub("backups"), "backup-1.db"), "backup");
        Directory.CreateDirectory(LegacySub("logs"));
        File.WriteAllText(Path.Combine(LegacySub("logs"), "unhandled.log"), "log");
    }

    [Fact]
    public void NoLegacyDirectory_ReportsNoLegacy_CreatesNothing()
    {
        var newRoot = Path.Combine(sandbox, "new-root");
        var result = DataRootMigrator.Migrate(newRoot, LegacyRoot);

        Assert.Equal(DataRootMigrationStatus.NoLegacy, result.Status);
        Assert.False(Directory.Exists(newRoot));
    }

    [Fact]
    public void Migrate_MovesAllSubDirectories_CleansLegacyRoot()
    {
        SeedLegacyData();
        var newRoot = Path.Combine(sandbox, "new-root");

        var result = DataRootMigrator.Migrate(newRoot, LegacyRoot);

        Assert.Equal(DataRootMigrationStatus.Migrated, result.Status);
        Assert.True(File.Exists(Path.Combine(newRoot, "Database", "gallery.db")));
        Assert.Equal("legacy-db-content", File.ReadAllText(Path.Combine(newRoot, "Database", "gallery.db")));
        Assert.True(File.Exists(Path.Combine(newRoot, "Images", "zhimi.heater.za1.png")));
        Assert.True(File.Exists(Path.Combine(newRoot, "Thumbnails", "thumb.webp")));
        Assert.True(File.Exists(Path.Combine(newRoot, "Backups", "backup-1.db")));
        Assert.True(File.Exists(Path.Combine(newRoot, "Logs", "unhandled.log")));
        Assert.False(Directory.Exists(LegacyRoot));
        Assert.NotNull(result.LogPath);
        Assert.True(File.Exists(result.LogPath));
    }

    [Fact]
    public void Migrate_IsIdempotent_AlreadyMigratedSkips()
    {
        SeedLegacyData();
        var newRoot = Path.Combine(sandbox, "new-root");
        Directory.CreateDirectory(Path.Combine(newRoot, "Database"));
        File.WriteAllText(Path.Combine(newRoot, "Database", "gallery.db"), "new-db");

        var result = DataRootMigrator.Migrate(newRoot, LegacyRoot);

        Assert.Equal(DataRootMigrationStatus.AlreadyMigrated, result.Status);
        // 新数据不被触碰。
        Assert.Equal("new-db", File.ReadAllText(Path.Combine(newRoot, "Database", "gallery.db")));
    }

    [Fact]
    public void Migrate_ConflictingFiles_AreSkipped_NotOverwritten()
    {
        SeedLegacyData();
        var newRoot = Path.Combine(sandbox, "new-root");
        // 预置目标同名文件（precious 内容），迁移不得覆盖。
        Directory.CreateDirectory(Path.Combine(newRoot, "Images"));
        File.WriteAllText(Path.Combine(newRoot, "Images", "zhimi.heater.za1.png"), "precious-new-data");

        var result = DataRootMigrator.Migrate(newRoot, LegacyRoot);

        Assert.Equal("precious-new-data", File.ReadAllText(Path.Combine(newRoot, "Images", "zhimi.heater.za1.png")));
        Assert.Contains(result.SkippedEntries, e => e.Contains("zhimi.heater.za1.png"));
        // 其余无冲突目录照常迁移。
        Assert.True(File.Exists(Path.Combine(newRoot, "Database", "gallery.db")));
    }

    [Fact]
    public void Migrate_PartialFailure_KeepsLegacyRoot_AndWritesLog()
    {
        SeedLegacyData();
        var newRoot = Path.Combine(sandbox, "new-root");
        // 目标位置预置同名"文件"（非目录），使 Images 子目录迁移必然失败。
        Directory.CreateDirectory(newRoot);
        File.WriteAllText(Path.Combine(newRoot, "Images"), "blocked");

        var result = DataRootMigrator.Migrate(newRoot, LegacyRoot);

        Assert.True(
            result.Status == DataRootMigrationStatus.Partial,
            $"status={result.Status} imagesFileExists={File.Exists(Path.Combine(newRoot, "Images"))} "
            + $"legacyImagesExists={Directory.Exists(LegacySub("images"))} "
            + $"moved=[{string.Join(",", result.MovedDirectories)}] skipped=[{string.Join(",", result.SkippedEntries)}]");
        // 失败项旧数据完整保留。
        Assert.True(Directory.Exists(LegacySub("images")));
        Assert.True(File.Exists(Path.Combine(LegacySub("images"), "zhimi.heater.za1.png")));
        // 成功项正常就位。
        Assert.True(File.Exists(Path.Combine(newRoot, "Database", "gallery.db")));
        // 日志落盘并记录失败。
        Assert.NotNull(result.LogPath);
        Assert.Contains("失败", File.ReadAllText(result.LogPath));
    }

    [Fact]
    public void Migrate_CrossVolume_FallsBackToCopy()
    {
        // 跨卷回退路径：直接对"目标已存在的父级"做逐文件移动（无法真实跨卷模拟），
        // 验证逐文件搬运语义：目标子目录已存在（非数据库）时文件逐个就位。
        SeedLegacyData();
        var newRoot = Path.Combine(sandbox, "new-root");
        Directory.CreateDirectory(Path.Combine(newRoot, "Images"));
        File.WriteAllText(Path.Combine(newRoot, "Images", "existing.txt"), "keep");

        var result = DataRootMigrator.Migrate(newRoot, LegacyRoot);

        Assert.True(File.Exists(Path.Combine(newRoot, "Images", "zhimi.heater.za1.png")));
        Assert.True(File.Exists(Path.Combine(newRoot, "Images", "existing.txt")));
        Assert.Contains(result.MovedDirectories, d => d == "images");
    }
}
