using MijiaProductGallery.Core.Interfaces;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using MijiaProductGallery.Tests.Database;
using Xunit;

namespace MijiaProductGallery.Tests;

/// <summary>搜索服务测试：七字段覆盖、大小写、转义、边界与排序优先级（真实 SQLite）。</summary>
public sealed class SearchServiceTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();
        var products = new ProductRepository(context);
        await products.AddRangeAsync(
        [
            new Product
            {
                Model = "zhimi.heater.za1",
                Name = "智米电暖器智能版",
                Brand = "智米",
                Category = "环境电器",
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            },
            new Product
            {
                Model = "xiaomi.airp.mp5b",
                Name = "米家空气净化器 5 小米出品",
                Brand = "小米出品",
                Category = "个护与起居",
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            },
            new Product
            {
                Model = "chuangmi.camera.029a02",
                Name = "小米智能摄像机 云台版2K",
                Brand = "小米出品",
                Category = "安防",
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            },
            new Product
            {
                Model = "miwu.bed.1yznc",
                Name = "1+Y智能床",
                Brand = "1+Y",
                Category = "个护与起居",
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            },
            new Product
            {
                Model = "x_y.model.01",
                Name = "100%纯棉床品",
                Brand = "Percent",
                Category = "其他",
                FirstSeenUnix = 1,
                LastSeenUnix = 1,
            },
        ]);
    }

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    private SearchService CreateService()
    {
        return new SearchService(host.CreateContext());
    }

    [Fact]
    public async Task Search_ByModel_Fragment_Matches()
    {
        var results = await CreateService().SearchAsync("airp");
        var model = Assert.Single(results);
        Assert.Equal("xiaomi.airp.mp5b", model.Model);
    }

    [Fact]
    public async Task Search_ByName_Chinese_Matches()
    {
        var results = await CreateService().SearchAsync("电暖器");
        Assert.Equal("zhimi.heater.za1", Assert.Single(results).Model);
    }

    [Fact]
    public async Task Search_ByBrand_Matches()
    {
        var results = await CreateService().SearchAsync("智米");
        Assert.Equal("zhimi.heater.za1", Assert.Single(results).Model);
    }

    [Fact]
    public async Task Search_ByCategory_Matches()
    {
        var results = await CreateService().SearchAsync("安防");
        Assert.Equal("chuangmi.camera.029a02", Assert.Single(results).Model);
    }

    [Fact]
    public async Task Search_English_IsCaseInsensitive()
    {
        var results = await CreateService().SearchAsync("CAMERA");
        Assert.Equal("chuangmi.camera.029a02", Assert.Single(results).Model);
    }

    [Fact]
    public async Task Search_TrimsSurroundingWhitespace()
    {
        var results = await CreateService().SearchAsync("  空气  ");
        Assert.Contains(results, product => product.Model == "xiaomi.airp.mp5b");
    }

    [Fact]
    public async Task Search_EmptyKeyword_ReturnsEmpty()
    {
        Assert.Empty(await CreateService().SearchAsync(string.Empty));
    }

    [Fact]
    public async Task Search_WhitespaceKeyword_ReturnsEmpty()
    {
        Assert.Empty(await CreateService().SearchAsync("   "));
    }

    [Fact]
    public async Task Search_NullKeyword_ReturnsEmpty()
    {
        Assert.Empty(await CreateService().SearchAsync(null!));
    }

    [Fact]
    public async Task Search_VeryLongInput_DoesNotThrow()
    {
        var results = await CreateService().SearchAsync(new string('a', 10_000));
        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_SqlInjectionChars_TreatedLiterally()
    {
        var before = await new ProductRepository(host.CreateContext()).CountAsync();
        var results = await CreateService().SearchAsync("'; DROP TABLE Products;--");

        Assert.Empty(results);
        Assert.Equal(before, await new ProductRepository(host.CreateContext()).CountAsync());
    }

    [Fact]
    public async Task Search_PercentSign_IsLiteral()
    {
        var results = await CreateService().SearchAsync("100%");
        Assert.Equal("x_y.model.01", Assert.Single(results).Model);
    }

    [Fact]
    public async Task Search_Underscore_IsLiteral()
    {
        var results = await CreateService().SearchAsync("x_y");
        Assert.Equal("x_y.model.01", Assert.Single(results).Model);
    }

    [Fact]
    public async Task Search_SingleChineseCharacter_MatchesMultiple()
    {
        var results = await CreateService().SearchAsync("米");
        Assert.True(results.Count >= 2);
    }

    [Fact]
    public async Task Search_ModelMatch_RanksBeforeNameMatch()
    {
        await using var context = host.CreateContext();
        await new ProductRepository(context).AddAsync(new Product
        {
            Model = "mmm.other.01",
            Name = "airp 机器",
            Brand = "b",
            Category = "c",
            FirstSeenUnix = 1,
            LastSeenUnix = 1,
        });

        var results = await CreateService().SearchAsync("airp");

        Assert.True(results.Count >= 2);
        Assert.Equal("xiaomi.airp.mp5b", results[0].Model);
    }
}

/// <summary>搜索历史仓储测试：合并、裁剪、排序、清空。</summary>
public sealed class SearchHistoryRepositoryTests : IAsyncLifetime
{
    private readonly DatabaseTestHost host = DatabaseTestHost.CreateNotInitialized();

    public async Task InitializeAsync()
    {
        await using var context = host.CreateContext();
        await new DbInitializer(context, host.Paths).InitializeAsync();
    }

    public Task DisposeAsync()
    {
        host.Dispose();
        return Task.CompletedTask;
    }

    private SearchHistoryRepository CreateRepository()
    {
        return new SearchHistoryRepository(host.CreateContext());
    }

    [Fact]
    public async Task FirstSearch_Recorded_WithResultCount()
    {
        await CreateRepository().AddAsync("空气", 1_700_000_000, 32);

        var entry = Assert.Single(await CreateRepository().GetRecentAsync(10));
        Assert.Equal("空气", entry.Query);
        Assert.Equal(32, entry.ResultCount);
    }

    [Fact]
    public async Task RepeatSearch_MergesIntoSingleRow()
    {
        var repository = CreateRepository();
        await repository.AddAsync("空气", 1_700_000_000, 32);
        await repository.AddAsync("空气", 1_700_000_500, 30);

        var entry = Assert.Single(await repository.GetRecentAsync(10));
        Assert.Equal(30, entry.ResultCount);
        Assert.Equal(1_700_000_500, entry.CreatedUnix);
    }

    [Fact]
    public async Task OverLimit_TrimsToThirty_KeepsNewest()
    {
        var repository = CreateRepository();
        for (var i = 0; i < 35; i++)
        {
            await repository.AddAsync($"关键词{i:00}", 1_700_000_000 + i, i);
        }

        var recent = await repository.GetRecentAsync(100);
        Assert.Equal(30, recent.Count);
        Assert.Equal("关键词34", recent[0].Query);
        Assert.Equal("关键词05", recent[^1].Query);
    }

    [Fact]
    public async Task GetRecent_OrdersNewestFirst()
    {
        var repository = CreateRepository();
        await repository.AddAsync("摄像头", 1_700_000_200, 1);
        await repository.AddAsync("空气", 1_700_000_300, 2);
        await repository.AddAsync("小米电视", 1_700_000_100, 3);

        var recent = await repository.GetRecentAsync(10);
        Assert.Equal(["空气", "摄像头", "小米电视"], recent.Select(entry => entry.Query).ToList());
    }

    [Fact]
    public async Task Clear_RemovesEverything()
    {
        var repository = CreateRepository();
        await repository.AddAsync("空气", 1, 1);
        await repository.AddAsync("摄像头", 2, 2);

        await repository.ClearAsync();

        Assert.Empty(await repository.GetRecentAsync(10));
    }
}
