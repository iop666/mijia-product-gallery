using MijiaProductGallery.Infrastructure.Database;
using MijiaProductGallery.Infrastructure.Images;

namespace MijiaProductGallery.Tests.TestSupport;

/// <summary>图片测试宿主：临时数据根（仅目录，不建库）。</summary>
public sealed class ImageTestHost : IDisposable
{
    public ImageTestHost()
    {
        RootDirectory = Path.Combine(Path.GetTempPath(), "mpg-img-" + Guid.NewGuid().ToString("N"));
        Paths = new DatabasePaths(RootDirectory);
        Paths.EnsureDirectories();
    }

    public string RootDirectory { get; }

    public DatabasePaths Paths { get; }

    public ImageStore CreateStore()
    {
        return new ImageStore(Paths);
    }

    public ThumbnailService CreateThumbnailService()
    {
        return new ThumbnailService(Paths);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
