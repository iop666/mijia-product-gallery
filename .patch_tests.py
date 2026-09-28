import io

# ViewModelsTests 与 FilterTests 的 CreateViewModel/CreateQueryService 修正
p = 'src/MijiaProductGallery.Tests/ViewModelsTests.cs'
with io.open(p, encoding='utf-8') as f:
    text = f.read()
old = """        return new GalleryViewModel(
            repository ?? new ProductRepository(host.CreateContext()),
            NewQueue(),
            new ProductQueryService(factory, new FilterService(), new SortService()),
            new SearchHistoryRepository(host.CreateContext()),"""
new = """        return new GalleryViewModel(
            repository ?? new ProductRepository(host.CreateContext()),
            NewQueue(),
            new ProductQueryService(factory, new FilterService(), new SortService()),
            new RecentService(host.CreateContext()),
            new SearchHistoryRepository(host.CreateContext()),"""
assert old in text, p
text = text.replace(old, new)
# using
if 'using MijiaProductGallery.Infrastructure.Database.Repositories;' not in text:
    old2 = "using MijiaProductGallery.Infrastructure.Database;"
    new2 = "using MijiaProductGallery.Infrastructure.Database;\nusing MijiaProductGallery.Infrastructure.Database.Repositories;"
    assert old2 in text
    text = text.replace(old2, new2)
with io.open(p, 'w', encoding='utf-8', newline='') as f:
    f.write(text)
print('viewmodelstests ok')

p = 'src/MijiaProductGallery.Tests/FilterTests.cs'
with io.open(p, encoding='utf-8') as f:
    text = f.read()
old = """    private GalleryViewModel CreateViewModel()
    {
        var factory = new TestDbContextFactory(() => host.CreateContext());
        return new GalleryViewModel(
            new ProductRepository(host.CreateContext()),
            new ThumbnailLoadQueue(
                new ThumbnailService(host.Paths),
                InlineUiDispatcher.Instance,
                concurrency: 1),
            new ProductQueryService(factory, new FilterService(), new SortService()),
            new SearchHistoryRepository(host.CreateContext()),"""
new = """    private GalleryViewModel CreateViewModel()
    {
        var factory = new TestDbContextFactory(() => host.CreateContext());
        return new GalleryViewModel(
            new ProductRepository(host.CreateContext()),
            new ThumbnailLoadQueue(
                new ThumbnailService(host.Paths),
                InlineUiDispatcher.Instance,
                concurrency: 1),
            new ProductQueryService(factory, new FilterService(), new SortService()),
            new RecentService(host.CreateContext()),
            new SearchHistoryRepository(host.CreateContext()),"""
assert old in text, p
text = text.replace(old, new)
with io.open(p, 'w', encoding='utf-8', newline='') as f:
    f.write(text)
print('filtertests ok')
