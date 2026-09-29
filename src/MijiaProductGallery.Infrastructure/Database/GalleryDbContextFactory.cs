using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MijiaProductGallery.Infrastructure.Database;

/// <summary>dotnet-ef 设计时（迁移脚手架）使用的上下文工厂。</summary>
public sealed class GalleryDbContextFactory : IDesignTimeDbContextFactory<GalleryDbContext>
{
    public GalleryDbContext CreateDbContext(string[] args)
    {
        var paths = new DatabasePaths();
        var options = new DbContextOptionsBuilder<GalleryDbContext>()
            .UseSqlite($"Data Source={paths.DatabaseFile}")
            .Options;

        return new GalleryDbContext(options);
    }
}
