using System.Globalization;
using System.Text;
using CsvHelper;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Rules;
using MijiaProductGallery.Tests.TestSupport;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>
/// 冻结基准测试：以 2026-09-28 快照清单为不变输入，锁定数据口径与对比规则的总体行为。
/// </summary>
public sealed class Baseline20260928Tests
{
    public const int ExpectedTotal = 10_547;
    public const int ExpectedAvailable = 10_533;
    public const int ExpectedDelisted = 14;

    [Fact]
    public void Rows_MatchSnapshotCounts()
    {
        var rows = BaselineCatalog.Rows;

        Assert.Equal(ExpectedTotal, rows.Count);
        Assert.Equal(
            ExpectedAvailable,
            rows.Count(row => !string.Equals(row.Remark, BaselineCatalog.DelistedRemark, StringComparison.Ordinal)));
        Assert.Equal(
            ExpectedDelisted,
            rows.Count(row => string.Equals(row.Remark, BaselineCatalog.DelistedRemark, StringComparison.Ordinal)));
    }

    [Fact]
    public void Rows_CoverExactlyTheRepositoryCategories()
    {
        var categories = BaselineCatalog.Rows
            .Select(row => row.Category)
            .Distinct()
            .OrderBy(category => category, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            BaselineCatalog.ExpectedCategories.OrderBy(category => category, StringComparer.Ordinal),
            categories);
    }

    [Fact]
    public void ShelfCategory_HoldsOnlyTheUnclassifiedModel()
    {
        var shelfRows = BaselineCatalog.Rows
            .Where(row => row.Category == CategoryRules.ShelfCategoryName)
            .ToList();

        var row = Assert.Single(shelfRows);
        Assert.Equal("xiaomi.airp.mp5b", row.Model);
    }

    [Fact]
    public void ModelWithoutImage_RegisteredWithEmptyFileName()
    {
        var row = Assert.Single(
            BaselineCatalog.Rows,
            candidate => candidate.Model == "ows.heater.pdeh1a");

        Assert.Equal(string.Empty, row.ImageFileName);
        Assert.NotEqual(BaselineCatalog.DelistedRemark, row.Remark);
    }

    [Fact]
    public void AuxModels_RegisteredInCsv_AndMapToSafeDiskNames()
    {
        foreach (var model in new[] { "aux.aircondition.hc1", "aux.aircondition.v1" })
        {
            var row = Assert.Single(
                BaselineCatalog.Rows,
                candidate => candidate.Model == model);

            Assert.Equal($"{model}.png", row.ImageFileName);
            Assert.True(FileNameRules.IsReservedDeviceModel(model));
            Assert.Equal(
                $"aux_.{model["aux.".Length..]}.png",
                FileNameRules.ToDiskFileName(row.ImageFileName));
        }
    }

    [Fact]
    public void DelistedManifest_MatchesBaselineDelistedRows()
    {
        var manifestModels = LoadDelistedManifestModels();
        var baselineDelistedModels = BaselineCatalog.Rows
            .Where(row => string.Equals(row.Remark, BaselineCatalog.DelistedRemark, StringComparison.Ordinal))
            .Select(row => row.Model)
            .OrderBy(model => model, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(ExpectedDelisted, manifestModels.Count);
        Assert.Equal(
            manifestModels.OrderBy(model => model, StringComparer.Ordinal),
            baselineDelistedModels);
    }

    [Fact]
    public void BaselineAgainstItself_YieldsExactlyTheDelistedChanges()
    {
        var rows = BaselineCatalog.Rows;
        var local = BaselineCatalog.ToLocalStates(rows);
        var remote = BaselineCatalog.ToRemoteStates(rows);
        var delistedManifest = LoadDelistedManifestModels();

        var diff = ProductCompareRules.CompareCatalog(local, remote);

        Assert.Equal(ExpectedDelisted, diff.Changes.Count);
        Assert.Equal(ExpectedDelisted, diff.Count(ChangeType.Delisted));
        Assert.Equal(
            delistedManifest.OrderBy(model => model, StringComparer.Ordinal),
            diff.Changes.Select(change => change.Model));
        foreach (var change in diff.Changes)
        {
            Assert.Equal(ChangeType.Delisted, change.Type);
            Assert.Null(change.ReplacedByOldFileName);
        }
    }

    [Fact]
    public void BaselineDiff_IsDeterministic()
    {
        var rows = BaselineCatalog.Rows;
        var local = BaselineCatalog.ToLocalStates(rows);
        var remote = BaselineCatalog.ToRemoteStates(rows);

        var first = ProductCompareRules.CompareCatalog(local, remote);
        var second = ProductCompareRules.CompareCatalog(local, remote);

        Assert.Equal(first.Changes, second.Changes);
    }

    private static List<string> LoadDelistedManifestModels()
    {
        using var reader = new StreamReader(
            Path.Combine(TestPaths.FixturesRoot, "baseline-2026-09-28", "下架清单.csv"),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        var models = new List<string>();
        foreach (var row in csv.GetRecords<dynamic>())
        {
            var record = (IDictionary<string, object>)row;
            models.Add(((string)record["型号"]).Trim());
        }

        return models;
    }
}
