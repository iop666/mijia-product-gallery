using Microsoft.EntityFrameworkCore;

namespace MijiaProductGallery.Infrastructure.Database;

/// <summary>
/// 仓储的单次操作上下文：应用内优先经工厂新建（操作结束即释放，互不共享）；
/// 单元测试直接传入上下文时回落到该实例（不负责释放）。
/// </summary>
internal readonly struct DbContextScope : IAsyncDisposable
{
    private readonly GalleryDbContext? owned;

    public DbContextScope(IDbContextFactory<GalleryDbContext>? factory, GalleryDbContext? shared)
    {
        owned = factory?.CreateDbContext();
        Context = owned ?? shared ?? throw new InvalidOperationException("数据库上下文未配置");
    }

    public GalleryDbContext Context { get; }

    public ValueTask DisposeAsync() => owned?.DisposeAsync() ?? ValueTask.CompletedTask;
}
