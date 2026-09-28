using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Core.Rules;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>产品对比规则的单型号判定测试（覆盖全部变更类型与保守回退）。</summary>
public sealed class ProductCompareRulesTests
{
    private const string DefaultModel = "chuangmi.camera.029a02";
    private const string DefaultImageFile = DefaultModel + ".png";
    private const string ShaA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ShaB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void NewProduct_ProducesNewChange()
    {
        var remote = Remote("xiaomi.curtain.0303", name: "米家智能窗帘 3", brand: "小米出品", category: "个护与起居");

        var diff = ProductCompareRules.CompareCatalog([], [remote]);

        var change = Assert.Single(diff.Changes);
        Assert.Equal(ChangeType.New, change.Type);
        Assert.Equal("xiaomi.curtain.0303", change.Model);
        Assert.Equal("个护与起居", change.NewCategory);
        Assert.Equal("米家智能窗帘 3", change.NewName);
        Assert.Equal("小米出品", change.NewBrand);
    }

    [Fact]
    public void DelistedProduct_MarkedAndKept()
    {
        var local = Local(
            "midjd.fridge.bs42s",
            name: "米家冰箱 巨能装Pro 十字513L",
            category: "厨房电器",
            imageFile: "midjd.fridge.bs42s.png");

        var diff = ProductCompareRules.CompareCatalog([local], []);

        var change = Assert.Single(diff.Changes);
        Assert.Equal(ChangeType.Delisted, change.Type);
        Assert.Equal("midjd.fridge.bs42s", change.Model);
        Assert.Equal("厨房电器", change.OldCategory);
        Assert.Equal("midjd.fridge.bs42s.png", change.ImageFileName);
        Assert.Null(change.ReplacedByOldFileName);
    }

    [Fact]
    public void CategoryChanged_ProducesCategoryChange()
    {
        var local = Local(sha: ShaA);
        var remote = Remote(category: "厨房电器");
        var review = Review(downloadedSha: ShaA);

        var change = ProductCompareRules.CompareProduct(local, remote, review);

        Assert.NotNull(change);
        Assert.Equal(ChangeType.CategoryChanged, change.Type);
        Assert.Equal("安防", change.OldCategory);
        Assert.Equal("厨房电器", change.NewCategory);
        Assert.Equal(ImageOutcome.None, change.ImageOutcome);
    }

    [Fact]
    public void NameTweak_WhitespaceOnly_RecordedAsNameChanged()
    {
        var local = Local(name: "小米音箱", sha: ShaA);
        var remote = Remote(name: "小米 音箱");

        var change = ProductCompareRules.CompareProduct(local, remote, Review(ShaA));

        Assert.NotNull(change);
        Assert.Equal(ChangeType.NameChanged, change.Type);
        Assert.Equal(NameChangeKind.Trivial, change.NameChange);
        Assert.Equal(ImageOutcome.None, change.ImageOutcome);
        Assert.Equal("小米音箱", change.OldName);
        Assert.Equal("小米 音箱", change.NewName);
    }

    [Fact]
    public void NameTweak_TrailingSpaceOnly_IsNoChange()
    {
        var change = ProductCompareRules.CompareProduct(
            Local(name: "小米音箱", sha: ShaA),
            Remote(name: "小米音箱 "),
            Review(ShaA));

        Assert.Null(change);
    }

    [Fact]
    public void OfficialImageSwap_ShaDiff_SameName_ProducesImageChanged()
    {
        var local = Local(sha: ShaA);
        var remote = Remote();

        var change = ProductCompareRules.CompareProduct(local, remote, Review(ShaB));

        Assert.NotNull(change);
        Assert.Equal(ChangeType.ImageChanged, change.Type);
        Assert.Equal(ImageOutcome.ImageChanged, change.ImageOutcome);
        Assert.Equal(DefaultImageFile, change.ImageFileName);
        Assert.Null(change.ReplacedByOldFileName);
        Assert.Equal(NameChangeKind.None, change.NameChange);
    }

    [Fact]
    public void ShaSame_NoChange_ReturnsNull()
    {
        var change = ProductCompareRules.CompareProduct(Local(sha: ShaA), Remote(), Review(ShaA));

        Assert.Null(change);
    }

    [Fact]
    public void ImageWithoutDownload_IsPendingReview()
    {
        var change = ProductCompareRules.CompareProduct(Local(sha: ShaA), Remote(), Review(downloadedSha: null));

        Assert.NotNull(change);
        Assert.Equal(ChangeType.None, change.Type);
        Assert.Equal(ImageOutcome.PendingReview, change.ImageOutcome);
    }

    [Fact]
    public void IdReuse_DifferentDevice_KeepsOldImageAsOldFile()
    {
        var local = Local(name: "摄像机", sha: ShaA);
        var remote = Remote(name: "智能门锁");

        var change = ProductCompareRules.CompareProduct(local, remote, Review(ShaB));

        Assert.NotNull(change);
        Assert.Equal(ChangeType.IdReused, change.Type);
        Assert.Equal(ImageOutcome.IdReused, change.ImageOutcome);
        Assert.Equal(DefaultImageFile, change.ImageFileName);
        Assert.Equal(DefaultModel + ".old.png", change.ReplacedByOldFileName);
    }

    [Fact]
    public void IdReuse_SharedGenericPrefix_NotTreatedAsRename()
    {
        var local = Local(name: "小米智能摄像机", sha: ShaA);
        var remote = Remote(name: "小米智能门锁");

        var change = ProductCompareRules.CompareProduct(local, remote, Review(ShaB));

        Assert.NotNull(change);
        Assert.Equal(ChangeType.IdReused, change.Type);
    }

    [Fact]
    public void NameChanged_SameLine_ShaDiff_ProducesNameChanged()
    {
        var local = Local(name: "小米空气净化器", sha: ShaA);
        var remote = Remote(name: "小米空气净化器 4");

        var change = ProductCompareRules.CompareProduct(local, remote, Review(ShaB));

        Assert.NotNull(change);
        Assert.Equal(ChangeType.NameChanged, change.Type);
        Assert.Equal(NameChangeKind.SameLine, change.NameChange);
        Assert.Equal(ImageOutcome.ImageChanged, change.ImageOutcome);
        Assert.Null(change.ReplacedByOldFileName);
    }

    [Fact]
    public void IdReuse_CreateTimeChanged_WinsOverSameLineName()
    {
        var local = Local(name: "小米空气净化器", sha: ShaA, createTimeUnix: 1_500_000_000);
        var remote = Remote(name: "小米空气净化器 4", createTimeUnix: 1_780_000_000);

        var change = ProductCompareRules.CompareProduct(local, remote, Review(ShaB));

        Assert.NotNull(change);
        Assert.Equal(ChangeType.IdReused, change.Type);
    }

    [Fact]
    public void Ambiguous_BrandChanged_TreatedAsIdReuse()
    {
        var local = Local(name: "小米音箱", brand: "小米出品", sha: ShaA);
        var remote = Remote(name: "小米音箱 Pro", brand: "小米");

        var change = ProductCompareRules.CompareProduct(local, remote, Review(ShaB));

        Assert.NotNull(change);
        Assert.Equal(ChangeType.IdReused, change.Type);
        Assert.Equal("小米出品", change.OldBrand);
        Assert.Equal("小米", change.NewBrand);
    }

    [Fact]
    public void Ambiguous_EmptyName_TreatedAsIdReuse()
    {
        var local = Local(name: "未知设备", sha: ShaA);
        var remote = Remote(name: "");

        var change = ProductCompareRules.CompareProduct(local, remote, Review(ShaB));

        Assert.NotNull(change);
        Assert.Equal(ChangeType.IdReused, change.Type);
    }

    [Fact]
    public void RelistedModel_RestoresAvailability()
    {
        var local = Local(sha: ShaA, isAvailable: false);
        var remote = Remote();

        var change = ProductCompareRules.CompareProduct(local, remote, Review(ShaA));

        Assert.NotNull(change);
        Assert.Equal(ChangeType.Relisted, change.Type);
        Assert.Equal(ImageOutcome.None, change.ImageOutcome);
    }

    [Fact]
    public void IdReuse_OldImageChain_AppendsGenerations()
    {
        var local = Local(name: "摄像机", sha: ShaA);
        var remote = Remote(name: "智能门锁");

        var first = ProductCompareRules.CompareProduct(local, remote, Review(ShaB, existingOlds: []));
        var second = ProductCompareRules.CompareProduct(local, remote, Review(ShaB, existingOlds: [DefaultModel + ".old.png"]));
        var third = ProductCompareRules.CompareProduct(local, remote, Review(ShaB, existingOlds: [DefaultModel + ".old.png", DefaultModel + ".old1.png"]));

        Assert.Equal(DefaultModel + ".old.png", first!.ReplacedByOldFileName);
        Assert.Equal(DefaultModel + ".old1.png", second!.ReplacedByOldFileName);
        Assert.Equal(DefaultModel + ".old2.png", third!.ReplacedByOldFileName);
    }

    [Fact]
    public void CompareCatalog_IsDeterministicAndOrderedByModel()
    {
        var local = new List<LocalProductState>
        {
            Local("zzz.model.c", name: "旧设备A", sha: ShaA),
            Local("aaa.model.a"),
            Local("mmm.model.b", name: "将被下架"),
        };
        var remote = new List<RemoteProductState>
        {
            Remote("aaa.model.a"),
            Remote("mmm.model.b"),
            Remote("bbb.model.new", name: "全新产品", category: "照明"),
            Remote("zzz.model.c", name: "智能门锁"),
        };
        var reviews = new Dictionary<string, ImageReviewInput>
        {
            ["zzz.model.c"] = Review(ShaB),
        };

        var firstRun = ProductCompareRules.CompareCatalog(local, remote, reviews);
        var secondRun = ProductCompareRules.CompareCatalog(local, remote, reviews);

        Assert.Equal(firstRun.Changes, secondRun.Changes);
        var models = firstRun.Changes.Select(change => change.Model).ToList();
        Assert.Equal(models.OrderBy(model => model, StringComparer.Ordinal), models);
        Assert.Single(firstRun.Changes, change => change.Type == ChangeType.New);
        Assert.Single(firstRun.Changes, change => change.Type == ChangeType.IdReused);
        Assert.Single(firstRun.Changes, change => change.Type == ChangeType.NameChanged);
        Assert.Equal(3, firstRun.Changes.Count);
    }

    private static LocalProductState Local(
        string? model = null,
        string name = "小米智能摄像机",
        string brand = "小米出品",
        string category = "安防",
        string? imageFile = DefaultImageFile,
        string? sha = ShaA,
        bool isAvailable = true,
        long? createTimeUnix = 1_500_000_000)
    {
        return new LocalProductState
        {
            Model = model ?? DefaultModel,
            Name = name,
            Brand = brand,
            Category = category,
            ImageFileName = imageFile,
            ImageSha256 = sha,
            IsAvailable = isAvailable,
            CreateTimeUnix = createTimeUnix,
            UpdateTimeUnix = 1_700_000_000,
        };
    }

    private static RemoteProductState Remote(
        string? model = null,
        string name = "小米智能摄像机",
        string brand = "小米出品",
        string category = "安防",
        long createTimeUnix = 1_500_000_000)
    {
        return new RemoteProductState
        {
            Model = model ?? DefaultModel,
            Name = name,
            Brand = brand,
            Category = category,
            RealIconUrl = $"https://cdn.cnbj1.fds.api.mi-img.com/iotweb-product-center/{model ?? DefaultModel}.png",
            CreateTimeUnix = createTimeUnix,
            UpdateTimeUnix = 1_790_000_000,
        };
    }

    private static ImageReviewInput Review(string? downloadedSha, IReadOnlyList<string>? existingOlds = null)
    {
        return new ImageReviewInput
        {
            DownloadedSha256 = downloadedSha,
            ExistingOldImageFiles = existingOlds ?? [],
        };
    }
}
