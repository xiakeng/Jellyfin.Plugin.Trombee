using Jellyfin.Plugin.Trombee.Persistence;
using Jellyfin.Plugin.Trombee.Services;
using MediaBrowser.Controller.Library;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Trombee.Tests.Services;

public sealed class ActorsIndexServiceTests
{
    [Fact]
    public async Task StatusReportsActiveGenerationAndIncrementalWatermark()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            await store.InitializeAsync(CancellationToken.None);
            var generation = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            var watermark = new DateTimeOffset(2026, 7, 17, 3, 0, 0, TimeSpan.Zero);
            await store.ActivateRebuildAsync(generation, DateTimeOffset.UtcNow, watermark, CancellationToken.None);
            var service = new ActorsIndexService(store, Mock.Of<ILibraryManager>());

            var status = await service.GetStatusAsync(CancellationToken.None);

            Assert.True(status.HasActiveGeneration);
            Assert.Equal(generation, status.ActiveGenerationId);
            Assert.Equal(watermark, status.LastIncrementalWatermarkUtc);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ActorPageReadsPersistedIndexWithoutScanningJellyfinItems()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            await store.InitializeAsync(CancellationToken.None);
            var generation = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            var itemId = Guid.NewGuid();
            await store.ReplaceItemsAsync(
                generation,
                [
                    new IndexedMediaItem(
                        itemId,
                        itemId,
                        "Persisted Movie",
                        "Movie",
                        2026,
                        DateTimeOffset.UtcNow,
                        [],
                        [new IndexedCredit("persisted", null, "Persisted Actor", null, "Actor")])
                ],
                CancellationToken.None);
            await store.ActivateRebuildAsync(generation, DateTimeOffset.UtcNow, CancellationToken.None);
            var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
            var service = new ActorsIndexService(store, libraryManager.Object);

            var page = await service.GetActorsIndexAsync(
                null,
                new ActorsQuery(0, 60, null, ActorSortBy.Name, SortDirection.Ascending, "Actor", [], 1),
                CancellationToken.None);

            Assert.Equal("Persisted Actor", Assert.Single(page.Actors).Name);
            libraryManager.VerifyNoOtherCalls();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
