using System.Runtime.CompilerServices;
using Jellyfin.Plugin.Trombee.Persistence;
using Jellyfin.Plugin.Trombee.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Trombee.Tests.Services;

public sealed class ActorsIndexMaintenanceServiceTests
{
    [Fact]
    public async Task FailedFullRebuildKeepsPreviousGenerationActive()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            await store.InitializeAsync(CancellationToken.None);
            var originalGeneration = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            await store.ReplaceItemsAsync(
                originalGeneration,
                [CreateMovie("old", "Old Actor")],
                CancellationToken.None);
            await store.ActivateRebuildAsync(originalGeneration, DateTimeOffset.UtcNow, CancellationToken.None);

            var source = new FakeActorsIndexSource
            {
                Items = [CreateMovie("new", "New Actor")],
                EnumerationFailure = new InvalidOperationException("Source scan failed.")
            };
            var service = new ActorsIndexMaintenanceService(
                store,
                source,
                new FixedTimeProvider(new DateTimeOffset(2026, 7, 17, 3, 0, 0, TimeSpan.Zero)),
                NullLogger<ActorsIndexMaintenanceService>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.FullRebuildAsync(null, CancellationToken.None));

            var page = await store.QueryActorsAsync(
                new ActorsQuery(0, 60, null, ActorSortBy.Name, SortDirection.Ascending, "Actor", [], 1),
                null,
                CancellationToken.None);
            Assert.Equal(originalGeneration, await store.GetActiveGenerationIdAsync(CancellationToken.None));
            Assert.Equal("Old Actor", Assert.Single(page.Actors).Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task IncrementalUpdateUsesOverlapAndReconcilesDeletedItems()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            var firstRun = new DateTimeOffset(2026, 7, 17, 3, 0, 0, TimeSpan.Zero);
            var secondRun = firstRun.AddHours(24);
            var clock = new FixedTimeProvider(firstRun);
            var retainedItem = CreateMovie("old", "Old Actor");
            var deletedItem = CreateMovie("deleted", "Deleted Actor");
            var source = new FakeActorsIndexSource
            {
                Items = [retainedItem, deletedItem]
            };
            var service = new ActorsIndexMaintenanceService(
                store,
                source,
                clock,
                NullLogger<ActorsIndexMaintenanceService>.Instance);
            await service.FullRebuildAsync(null, CancellationToken.None);

            var updatedItem = retainedItem with
            {
                Name = "Updated Movie",
                Credits = [new IndexedCredit("new", null, "New Actor", null, "Actor")]
            };
            source.ModifiedItems = [updatedItem];
            source.CurrentSourceItemIds = [retainedItem.SourceItemId];
            clock.SetUtcNow(secondRun);

            await service.IncrementalUpdateAsync(null, CancellationToken.None);

            var page = await store.QueryActorsAsync(
                new ActorsQuery(0, 60, null, ActorSortBy.Name, SortDirection.Ascending, "Actor", [], 1),
                null,
                CancellationToken.None);
            Assert.Equal(firstRun.AddMinutes(-5), Assert.Single(source.ModifiedSinceRequests));
            Assert.Equal(secondRun, await store.GetIncrementalWatermarkAsync(CancellationToken.None));
            Assert.Equal("New Actor", Assert.Single(page.Actors).Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LiveChangesUpdateActiveGenerationWithoutAdvancingWatermark()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            var initialTime = new DateTimeOffset(2026, 7, 17, 3, 0, 0, TimeSpan.Zero);
            var retainedItem = CreateMovie("old", "Old Actor");
            var deletedItem = CreateMovie("deleted", "Deleted Actor");
            var source = new FakeActorsIndexSource { Items = [retainedItem, deletedItem] };
            var service = new ActorsIndexMaintenanceService(
                store,
                source,
                new FixedTimeProvider(initialTime),
                NullLogger<ActorsIndexMaintenanceService>.Instance);
            await service.FullRebuildAsync(null, CancellationToken.None);

            source.Items =
            [
                retainedItem with
                {
                    Credits = [new IndexedCredit("live", null, "Live Actor", null, "Actor")]
                }
            ];
            await service.ProcessChangesAsync(
                [
                    new LibraryChange(retainedItem.SourceItemId, LibraryChangeKind.Updated),
                    new LibraryChange(deletedItem.SourceItemId, LibraryChangeKind.Removed)
                ],
                CancellationToken.None);

            var page = await store.QueryActorsAsync(
                new ActorsQuery(0, 60, null, ActorSortBy.Name, SortDirection.Ascending, "Actor", [], 1),
                null,
                CancellationToken.None);
            Assert.Equal("Live Actor", Assert.Single(page.Actors).Name);
            Assert.Equal(initialTime, await store.GetIncrementalWatermarkAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IndexedMediaItem CreateMovie(string actorKey, string actorName)
    {
        var id = Guid.NewGuid();
        return new IndexedMediaItem(
            id,
            id,
            actorName + " Movie",
            "Movie",
            2026,
            DateTimeOffset.UtcNow,
            [],
            [new IndexedCredit(actorKey, null, actorName, null, "Actor")]);
    }

    private sealed class FakeActorsIndexSource : IActorsIndexSource
    {
        public IReadOnlyList<IndexedMediaItem> Items { get; set; } = [];

        public IReadOnlyList<IndexedMediaItem> ModifiedItems { get; set; } = [];

        public IReadOnlyCollection<Guid>? CurrentSourceItemIds { get; set; }

        public List<DateTimeOffset?> ModifiedSinceRequests { get; } = [];

        public Exception? EnumerationFailure { get; init; }

        public Task<int> GetIndexableItemCountAsync(DateTimeOffset? modifiedSinceUtc, CancellationToken cancellationToken)
        {
            return Task.FromResult(modifiedSinceUtc is null ? Items.Count : ModifiedItems.Count);
        }

        public async IAsyncEnumerable<IndexedMediaItem> GetItemsAsync(
            DateTimeOffset? modifiedSinceUtc,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (modifiedSinceUtc is not null)
            {
                ModifiedSinceRequests.Add(modifiedSinceUtc);
            }
            var selectedItems = modifiedSinceUtc is null ? Items : ModifiedItems;
            foreach (var item in selectedItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }

            if (EnumerationFailure is not null)
            {
                throw EnumerationFailure;
            }
        }

        public Task<IReadOnlyCollection<Guid>> GetCurrentSourceItemIdsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(
                CurrentSourceItemIds
                ?? (IReadOnlyCollection<Guid>)Items.Select(item => item.SourceItemId).ToArray());
        }

        public Task<IndexedMediaItem?> GetItemAsync(Guid sourceItemId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Items.FirstOrDefault(item => item.SourceItemId == sourceItemId));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void SetUtcNow(DateTimeOffset value)
        {
            _utcNow = value;
        }
    }
}
