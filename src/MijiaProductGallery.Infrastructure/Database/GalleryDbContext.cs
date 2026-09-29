using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MijiaProductGallery.Core.Models;

namespace MijiaProductGallery.Infrastructure.Database;

/// <summary>图库主数据库上下文。表结构与索引配置见 EntityConfigurations。</summary>
public sealed class GalleryDbContext(DbContextOptions<GalleryDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<FavoriteItem> Favorites => Set<FavoriteItem>();

    public DbSet<ProductUsage> ProductUsages => Set<ProductUsage>();

    public DbSet<Collection> Collections => Set<Collection>();

    public DbSet<CollectionItem> CollectionItems => Set<CollectionItem>();

    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();

    public DbSet<SearchHistoryEntry> SearchHistories => Set<SearchHistoryEntry>();

    public DbSet<AppSettingEntry> AppSettings => Set<AppSettingEntry>();

    public DbSet<SyncState> SyncState => Set<SyncState>();

    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();

    public DbSet<SyncChange> SyncChanges => Set<SyncChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        EntityConfigurations.Apply(modelBuilder);
    }
}

/// <summary>全部实体的表名、键、索引与值转换配置。</summary>
public static class EntityConfigurations
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Apply(ModelBuilder builder)
    {
        builder.Entity<Product>(product =>
        {
            product.ToTable("Products");
            product.HasKey(p => p.Id);
            product.Property(p => p.Model).IsRequired();
            product.HasIndex(p => p.Model).IsUnique();
            product.HasIndex(p => p.RandomKey);
            product.HasIndex(p => p.Name);
            product.HasIndex(p => p.Brand);
            product.HasIndex(p => p.Category);
            product.HasIndex(p => p.IsAvailable);
            product.HasIndex(p => p.Sha256);
        });

        builder.Entity<FavoriteItem>(favorite =>
        {
            favorite.ToTable("Favorites");
            favorite.HasKey(f => f.ProductId);
            favorite.HasIndex(f => f.CreatedUnix);
            favorite
                .HasOne<Product>()
                .WithMany()
                .HasForeignKey(f => f.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProductUsage>(usage =>
        {
            usage.ToTable("ProductUsages");
            usage.HasKey(u => u.ProductId);
            usage.HasIndex(u => u.TotalUseCount);
            usage.HasIndex(u => u.LastUsedUnix);
            usage
                .HasOne<Product>()
                .WithMany()
                .HasForeignKey(u => u.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Collection>(collection =>
        {
            collection.ToTable("Collections");
            collection.HasKey(c => c.Id);
            collection.Property(c => c.Id).ValueGeneratedOnAdd();
            collection.HasIndex(c => c.Name).IsUnique();
        });

        builder.Entity<CollectionItem>(item =>
        {
            item.ToTable("CollectionItems");
            item.HasKey(i => new { i.CollectionId, i.ProductId });
            item.HasIndex(i => i.ProductId);
            item
                .HasOne<Collection>()
                .WithMany()
                .HasForeignKey(i => i.CollectionId)
                .OnDelete(DeleteBehavior.Cascade);
            item
                .HasOne<Product>()
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UsageEvent>(evt =>
        {
            evt.ToTable("UsageEvents");
            evt.HasKey(e => e.Id);
            evt.Property(e => e.Id).ValueGeneratedOnAdd();
            evt.HasIndex(e => e.UsedUnix);
            evt.HasIndex(e => new { e.ProductId, e.UsedUnix });
            evt
                .HasOne<Product>()
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SearchHistoryEntry>(history =>
        {
            history.ToTable("SearchHistories");
            history.HasKey(h => h.Id);
            history.Property(h => h.Id).ValueGeneratedOnAdd();
            history.HasIndex(h => h.Query).IsUnique();
            history.HasIndex(h => h.CreatedUnix);
            history.Property(h => h.ResultCount).IsRequired();
        });

        builder.Entity<AppSettingEntry>(setting =>
        {
            setting.ToTable("AppSettings");
            setting.HasKey(s => s.Key);
        });

        builder.Entity<SyncState>(state =>
        {
            state.ToTable("SyncState");
            state.HasKey(s => s.Id);
        });

        builder.Entity<SyncRun>(run =>
        {
            run.ToTable("SyncRuns");
            run.HasKey(r => r.Id);
            run.Property(r => r.Id).ValueGeneratedOnAdd();
            run.HasIndex(r => r.StartedUnix);
            run.Property(r => r.Counts)
                .HasConversion(
                    counts => counts == null ? null : JsonSerializer.Serialize(counts, JsonOptions),
                    json => string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<SyncRunCounts>(json, JsonOptions));
        });

        builder.Entity<SyncChange>(syncChange =>
        {
            syncChange.ToTable("SyncChanges");
            syncChange.HasKey(c => c.Id);
            syncChange.Property(c => c.Id).ValueGeneratedOnAdd();
            syncChange.HasIndex(c => c.Model);
            syncChange.HasIndex(c => c.Type);
            syncChange.HasIndex(c => c.SyncRunId);
            syncChange
                .HasOne<SyncRun>()
                .WithMany()
                .HasForeignKey(c => c.SyncRunId)
                .OnDelete(DeleteBehavior.Cascade);
            syncChange.Property(c => c.Change)
                .HasConversion(
                    value => JsonSerializer.Serialize(value, JsonOptions),
                    json => JsonSerializer.Deserialize<ProductChange>(json, JsonOptions)!)
                .HasColumnName("DetailJson");
        });
    }
}
