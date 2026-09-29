using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Enums;
using MijiaProductGallery.Core.Models;
using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Database.Repositories;
using Xunit;

namespace MijiaProductGallery.Tests.Database;

/// <summary>产品仓储测试：官方字段读写、唯一约束、索引存在与官方/用户数据隔离。</summary>
public sealed class ProductRepositoryTests : IDisposable
{
    private readonly DatabaseTestHost _host = DatabaseTestHost.CreateNotInitialized();

    [Fact]
    public async Task AddAndGetByModel_RoundTripsAllOfficialFields()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();
        var repository = new ProductRepository(context);

        await repository.AddAsync(MakeProduct());

        var loaded = await repository.GetByModelAsync("zhimi.heater.za1");
        Assert.NotNull(loaded);
        Assert.Equal("米家智能电暖器", loaded.Name);
        Assert.Equal("小米出品", loaded.Brand);
        Assert.Equal("环境电器", loaded.Category);
        Assert.Equal("zhimi.heater.za1.png", loaded.ImageFileName);
        Assert.Equal("images/zhimi.heater.za1.png", loaded.ImagePath);
        Assert.Equal(ImageFormat.Png, loaded.ImageFormat);
        Assert.Equal(480, loaded.ImageWidth);
        Assert.Equal(480, loaded.ImageHeight);
        Assert.Equal(123_456, loaded.FileSize);
        Assert.Equal(Sha, loaded.Sha256);
        Assert.True(loaded.IsAvailable);
        Assert.Equal(1_500_000_000, loaded.CreateTimeUnix);
        Assert.Equal(1_600_000_000, loaded.UpdateTimeUnix);
        Assert.Equal(1_700_000_000, loaded.FirstSeenUnix);
        Assert.Equal(1_700_000_000, loaded.LastSeenUnix);
    }

    [Fact]
    public async Task ModelUniqueIndex_RejectsDuplicateModel()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();
        var repository = new ProductRepository(context);

        await repository.AddAsync(MakeProduct());

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.AddAsync(MakeProduct()));
    }

    [Fact]
    public async Task ProductsTable_HasAllDocumentedIndexes()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();

        var indexNames = await QueryIndexNamesAsync(context, "Products");

        Assert.Subset(
            indexNames,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "IX_Products_Model",
                "IX_Products_Name",
                "IX_Products_Brand",
                "IX_Products_Category",
                "IX_Products_IsAvailable",
                "IX_Products_Sha256",
            });
    }

    [Fact]
    public async Task UpdateOfficialFields_ChangesOfficialColumns_KeepsFirstSeen()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();
        var repository = new ProductRepository(context);

        var product = MakeProduct();
        await repository.AddAsync(product);

        product.Name = "米家智能电暖器 2";
        product.Category = "厨房电器";
        product.Sha256 = "newsha";
        product.IsAvailable = false;
        product.LastSeenUnix = 1_800_000_000;
        await repository.UpdateOfficialFieldsAsync(product);

        var loaded = await repository.GetByModelAsync("zhimi.heater.za1");
        Assert.NotNull(loaded);
        Assert.Equal("米家智能电暖器 2", loaded.Name);
        Assert.Equal("厨房电器", loaded.Category);
        Assert.Equal("newsha", loaded.Sha256);
        Assert.False(loaded.IsAvailable);
        Assert.Equal(1_800_000_000, loaded.LastSeenUnix);
        Assert.Equal(1_700_000_000, loaded.FirstSeenUnix);
    }

    [Fact]
    public async Task UpdateOfficialFields_LeavesUserDataUntouched()
    {
        await using var context = _host.CreateContext();
        await _host.CreateInitializer(context).InitializeAsync();
        var products = new ProductRepository(context);
        var favorites = new FavoritesRepository(context);
        var usages = new UsageRepository(context);

        var product = MakeProduct();
        await products.AddAsync(product);
        await favorites.AddAsync(product.Id, 1_700_000_100);
        await usages.RecordAsync(product.Id, UsageType.Copy, 1_700_000_200);
        await usages.RecordAsync(product.Id, UsageType.Drag, 1_700_000_300);

        product.Name = "官方改名";
        product.Category = "厨房电器";
        product.Sha256 = "newsha";
        product.IsAvailable = false;
        await products.UpdateOfficialFieldsAsync(product);

        var usage = await usages.GetCountsAsync(product.Id);
        Assert.NotNull(usage);
        Assert.Equal(1, usage.CopyCount);
        Assert.Equal(1, usage.DragCount);
        Assert.Equal(2, usage.TotalUseCount);
        Assert.Equal(1_700_000_300, usage.LastUsedUnix);
        Assert.True(await favorites.IsFavoriteAsync(product.Id));
        Assert.Equal(2, await context.UsageEvents.CountAsync());
        Assert.Equal(1, await context.Favorites.CountAsync());
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static Product MakeProduct()
    {
        return new Product
        {
            Model = "zhimi.heater.za1",
            Name = "米家智能电暖器",
            Brand = "小米出品",
            Category = "环境电器",
            ImageFileName = "zhimi.heater.za1.png",
            ImagePath = "images/zhimi.heater.za1.png",
            ImageUrl = "https://cdn.cnbj1.fds.api.mi-img.com/x.png",
            ImageFormat = ImageFormat.Png,
            ImageWidth = 480,
            ImageHeight = 480,
            FileSize = 123_456,
            Sha256 = Sha,
            IsAvailable = true,
            CreateTimeUnix = 1_500_000_000,
            UpdateTimeUnix = 1_600_000_000,
            FirstSeenUnix = 1_700_000_000,
            LastSeenUnix = 1_700_000_000,
        };
    }

    private static async Task<HashSet<string>> QueryIndexNamesAsync(GalleryDbContext context, string tableName)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = '{tableName}';";
        await context.Database.OpenConnectionAsync();
        await using var reader = await command.ExecuteReaderAsync();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
