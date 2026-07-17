using Jellyfin.Plugin.Trombee.Persistence;
using Jellyfin.Plugin.Trombee.Persistence.Sql;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jellyfin.Plugin.Trombee.Tests.Persistence;

public sealed class SqliteActorsIndexStoreTests
{
    [Fact]
    public async Task SchemaUsesFilmographyIndexOrder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "actors-index.db");

        try
        {
            await using var store = new SqliteActorsIndexStore(databasePath);
            await store.InitializeAsync(CancellationToken.None);

            using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync(CancellationToken.None);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM pragma_index_info('idx_credits_actor_query') ORDER BY seqno;";
            using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
            var columns = new List<string>();
            while (await reader.ReadAsync(CancellationToken.None))
            {
                columns.Add(reader.GetString(0));
            }

            Assert.Equal(
                ["generation_id", "actor_key", "person_type", "source_item_id"],
                columns);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RebuildActivationUpdatesPlannerStatistics()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "actors-index.db");

        try
        {
            await using var store = new SqliteActorsIndexStore(databasePath);
            await store.InitializeAsync(CancellationToken.None);
            var generation = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            await store.ReplaceItemsAsync(
                generation,
                [CreateMovie(Guid.NewGuid(), "Analyzed Movie", "analyzed", "Analyzed Actor")],
                CancellationToken.None);

            await store.ActivateRebuildAsync(generation, DateTimeOffset.UtcNow, CancellationToken.None);

            Assert.Equal(
                ["credits", "media_items"],
                await GetTablesWithPlannerStatisticsAsync(databasePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task IncrementalCompletionUpdatesPlannerStatistics()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "actors-index.db");

        try
        {
            await using var store = new SqliteActorsIndexStore(databasePath);
            await store.InitializeAsync(CancellationToken.None);
            var generation = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            await store.ReplaceItemsAsync(
                generation,
                [CreateMovie(Guid.NewGuid(), "Analyzed Movie", "analyzed", "Analyzed Actor")],
                CancellationToken.None);
            await store.ActivateRebuildAsync(generation, DateTimeOffset.UtcNow, CancellationToken.None);

            using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync(CancellationToken.None);
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    DELETE FROM sqlite_stat1;
                    ANALYZE sqlite_schema;
                    """;
                _ = await command.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await store.CompleteIncrementalAsync(DateTimeOffset.UtcNow, CancellationToken.None);

            Assert.Equal(
                ["credits", "media_items"],
                await GetTablesWithPlannerStatisticsAsync(databasePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SchemaInitializationReportsCreationThenReuse()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "actors-index.db");
        var resources = new EmbeddedSqlResourceProvider(typeof(SqliteActorsIndexStore).Assembly);

        try
        {
            await using (var firstStore = new SqliteActorsIndexStore(databasePath, resources))
            {
                Assert.Equal(
                    SchemaInitializationResult.Created,
                    await firstStore.InitializeAsync(CancellationToken.None));
            }

            await using (var reopenedStore = new SqliteActorsIndexStore(databasePath, resources))
            {
                Assert.Equal(
                    SchemaInitializationResult.Unchanged,
                    await reopenedStore.InitializeAsync(CancellationToken.None));
            }

            using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync(CancellationToken.None);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT schema_hash FROM schema_metadata WHERE singleton_id = 1;";
            Assert.Equal(resources.SchemaHash, await command.ExecuteScalarAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SchemaHashMismatchRecreatesTheDerivedDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "actors-index.db");
        var resources = new EmbeddedSqlResourceProvider(typeof(SqliteActorsIndexStore).Assembly);

        try
        {
            await using (var originalStore = new SqliteActorsIndexStore(databasePath, resources))
            {
                _ = await originalStore.InitializeAsync(CancellationToken.None);
                var generation = await originalStore.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
                await originalStore.ReplaceItemsAsync(
                    generation,
                    [CreateMovie(Guid.NewGuid(), "Preserved Movie", "preserved", "Preserved Actor")],
                    CancellationToken.None);
                await originalStore.ActivateRebuildAsync(generation, DateTimeOffset.UtcNow, CancellationToken.None);
            }

            using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync(CancellationToken.None);
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE schema_metadata SET schema_hash = 'different' WHERE singleton_id = 1;";
                _ = await command.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await using var recreatedStore = new SqliteActorsIndexStore(databasePath, resources);
            Assert.Equal(
                SchemaInitializationResult.Recreated,
                await recreatedStore.InitializeAsync(CancellationToken.None));
            Assert.False(await recreatedStore.HasActiveGenerationAsync(CancellationToken.None));
            var page = await recreatedStore.QueryActorsAsync(
                new ActorsQuery(0, 60, null, ActorSortBy.Name, SortDirection.Ascending, "Actor", [], 1),
                null,
                CancellationToken.None);
            Assert.Empty(page.Actors);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FailedSchemaReplacementPreservesTheExistingDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "actors-index.db");
        var resources = new EmbeddedSqlResourceProvider(typeof(SqliteActorsIndexStore).Assembly);

        try
        {
            await using (var originalStore = new SqliteActorsIndexStore(databasePath, resources))
            {
                _ = await originalStore.InitializeAsync(CancellationToken.None);
                var generation = await originalStore.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
                await originalStore.ReplaceItemsAsync(
                    generation,
                    [CreateMovie(Guid.NewGuid(), "Preserved Movie", "preserved", "Preserved Actor")],
                    CancellationToken.None);
                await originalStore.ActivateRebuildAsync(generation, DateTimeOffset.UtcNow, CancellationToken.None);
            }

            using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync(CancellationToken.None);
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE schema_metadata SET schema_hash = 'different' WHERE singleton_id = 1;";
                _ = await command.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await using var invalidStore = new SqliteActorsIndexStore(
                databasePath,
                new InvalidSchemaResourceProvider(resources));
            _ = await Assert.ThrowsAsync<SqliteException>(
                () => invalidStore.InitializeAsync(CancellationToken.None));

            using var preservedConnection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await preservedConnection.OpenAsync(CancellationToken.None);
            using var preservedCommand = preservedConnection.CreateCommand();
            preservedCommand.CommandText =
                "SELECT COUNT(*) FROM credits WHERE name = 'Preserved Actor';";
            Assert.Equal(1L, await preservedCommand.ExecuteScalarAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NewStoreHasNoActiveGeneration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));

            await store.InitializeAsync(CancellationToken.None);

            Assert.False(await store.HasActiveGenerationAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RebuildRemainsInactiveUntilItIsActivated()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            await store.InitializeAsync(CancellationToken.None);

            var firstGeneration = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            await store.ActivateRebuildAsync(firstGeneration, DateTimeOffset.UtcNow, CancellationToken.None);
            var secondGeneration = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);

            Assert.Equal(firstGeneration, await store.GetActiveGenerationIdAsync(CancellationToken.None));

            await store.ActivateRebuildAsync(secondGeneration, DateTimeOffset.UtcNow, CancellationToken.None);

            Assert.Equal(secondGeneration, await store.GetActiveGenerationIdAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ActorPageDeduplicatesEpisodesAndIgnoresInactiveGeneration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            await store.InitializeAsync(CancellationToken.None);
            var generation = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            var seriesId = Guid.NewGuid();
            var personId = Guid.NewGuid();
            var credit = new IndexedCredit(personId.ToString("N"), personId, "Jane Doe", "Lead", "Actor");

            await store.ReplaceItemsAsync(
                generation,
                [
                    new IndexedMediaItem(Guid.NewGuid(), seriesId, "Example Series", "Series", 2024, DateTimeOffset.UtcNow, [Guid.NewGuid()], [credit]),
                    new IndexedMediaItem(Guid.NewGuid(), seriesId, "Example Series", "Series", 2024, DateTimeOffset.UtcNow, [Guid.NewGuid()], [credit])
                ],
                CancellationToken.None);
            await store.ActivateRebuildAsync(generation, DateTimeOffset.UtcNow, CancellationToken.None);

            var inactiveGeneration = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            await store.ReplaceItemsAsync(
                inactiveGeneration,
                [new IndexedMediaItem(Guid.NewGuid(), Guid.NewGuid(), "Hidden Movie", "Movie", 2025, DateTimeOffset.UtcNow, [], [new IndexedCredit("hidden", null, "Hidden Actor", null, "Actor")])],
                CancellationToken.None);

            var page = await store.QueryActorsAsync(
                new ActorsQuery(0, 60, null, ActorSortBy.Appearances, SortDirection.Descending, "Actor", [], 1),
                null,
                CancellationToken.None);

            Assert.Equal(1, page.TotalRecordCount);
            var actor = Assert.Single(page.Actors);
            Assert.Equal("Jane Doe", actor.Name);
            Assert.Equal(1, actor.Appearances);
            Assert.Equal(personId, actor.PersonId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task VisibilityFilterIsAppliedBeforeCountsAndPaging()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            await store.InitializeAsync(CancellationToken.None);
            var generation = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            var visibleAliceItem = Guid.NewGuid();
            var hiddenAliceItem = Guid.NewGuid();
            var visibleBobItem = Guid.NewGuid();

            await store.ReplaceItemsAsync(
                generation,
                [
                    CreateMovie(visibleAliceItem, "Visible Alice", "alice", "Alice"),
                    CreateMovie(hiddenAliceItem, "Hidden Alice", "alice", "Alice"),
                    CreateMovie(visibleBobItem, "Visible Bob", "bob", "Bob")
                ],
                CancellationToken.None);
            await store.ActivateRebuildAsync(generation, DateTimeOffset.UtcNow, CancellationToken.None);

            var firstPage = await store.QueryActorsAsync(
                new ActorsQuery(0, 1, null, ActorSortBy.Name, SortDirection.Ascending, "Actor", [], 1),
                [visibleAliceItem, visibleBobItem],
                CancellationToken.None);
            var secondPage = await store.QueryActorsAsync(
                new ActorsQuery(1, 1, null, ActorSortBy.Name, SortDirection.Ascending, "Actor", [], 1),
                [visibleAliceItem, visibleBobItem],
                CancellationToken.None);
            var emptyPage = await store.QueryActorsAsync(
                new ActorsQuery(0, 60, null, ActorSortBy.Name, SortDirection.Ascending, "Actor", [], 1),
                [],
                CancellationToken.None);

            Assert.Equal(2, firstPage.TotalRecordCount);
            Assert.Equal("Alice", Assert.Single(firstPage.Actors).Name);
            Assert.Equal(1, firstPage.Actors[0].Appearances);
            Assert.Equal(2, secondPage.TotalRecordCount);
            Assert.Equal("Bob", Assert.Single(secondPage.Actors).Name);
            Assert.Equal(0, emptyPage.TotalRecordCount);
            Assert.Empty(emptyPage.Actors);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ActorSearchTreatsSqlWildcardCharactersLiterally()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            await store.InitializeAsync(CancellationToken.None);
            var generation = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            await store.ReplaceItemsAsync(
                generation,
                [
                    CreateMovie(Guid.NewGuid(), "Percent Movie", "percent", "100% Real"),
                    CreateMovie(Guid.NewGuid(), "Digits Movie", "digits", "1000 Real")
                ],
                CancellationToken.None);
            await store.ActivateRebuildAsync(generation, DateTimeOffset.UtcNow, CancellationToken.None);

            var page = await store.QueryActorsAsync(
                new ActorsQuery(0, 60, "%", ActorSortBy.Name, SortDirection.Ascending, "Actor", [], 1),
                null,
                CancellationToken.None);

            Assert.Equal("100% Real", Assert.Single(page.Actors).Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryFilterIsAppliedBeforeActorAggregation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            await store.InitializeAsync(CancellationToken.None);
            var generation = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            var selectedLibraryId = Guid.NewGuid();
            var otherLibraryId = Guid.NewGuid();
            await store.ReplaceItemsAsync(
                generation,
                [
                    CreateMovie(Guid.NewGuid(), "Selected Movie", "selected", "Selected Actor", [selectedLibraryId]),
                    CreateMovie(Guid.NewGuid(), "Other Movie", "other", "Other Actor", [otherLibraryId])
                ],
                CancellationToken.None);
            await store.ActivateRebuildAsync(generation, DateTimeOffset.UtcNow, CancellationToken.None);

            var page = await store.QueryActorsAsync(
                new ActorsQuery(0, 60, null, ActorSortBy.Name, SortDirection.Ascending, "Actor", [selectedLibraryId], 1),
                null,
                CancellationToken.None);

            Assert.Equal("Selected Actor", Assert.Single(page.Actors).Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task IncrementalChangesMutateActiveGenerationAndAdvanceWatermark()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            await store.InitializeAsync(CancellationToken.None);
            var generation = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            var replacedItemId = Guid.NewGuid();
            var deletedItemId = Guid.NewGuid();
            await store.ReplaceItemsAsync(
                generation,
                [
                    CreateMovie(replacedItemId, "Old Movie", "old", "Old Actor"),
                    CreateMovie(deletedItemId, "Deleted Movie", "deleted", "Deleted Actor")
                ],
                CancellationToken.None);
            await store.ActivateRebuildAsync(generation, DateTimeOffset.UtcNow, CancellationToken.None);

            await store.ReplaceItemsAsync(
                generation,
                [CreateMovie(replacedItemId, "Updated Movie", "new", "New Actor")],
                CancellationToken.None);
            await store.DeleteItemsAsync(generation, [deletedItemId], CancellationToken.None);
            var watermark = new DateTimeOffset(2026, 7, 17, 3, 0, 0, TimeSpan.Zero);
            await store.CompleteIncrementalAsync(watermark, CancellationToken.None);

            var page = await store.QueryActorsAsync(
                new ActorsQuery(0, 60, null, ActorSortBy.Name, SortDirection.Ascending, "Actor", [], 1),
                null,
                CancellationToken.None);
            var sourceItemIds = await store.GetSourceItemIdsAsync(generation, CancellationToken.None);

            Assert.Equal(generation, await store.GetActiveGenerationIdAsync(CancellationToken.None));
            Assert.Equal(watermark, await store.GetIncrementalWatermarkAsync(CancellationToken.None));
            Assert.Equal("New Actor", Assert.Single(page.Actors).Name);
            Assert.Equal([replacedItemId], sourceItemIds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FilmographyDeduplicatesEpisodesAndPagesAfterSorting()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            await store.InitializeAsync(CancellationToken.None);
            var generation = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            var seriesId = Guid.NewGuid();
            var actorKey = "filmography-actor";
            await store.ReplaceItemsAsync(
                generation,
                [
                    CreateDisplayItem(Guid.NewGuid(), seriesId, "Recent Series", "Series", 2025, actorKey, "Lead"),
                    CreateDisplayItem(Guid.NewGuid(), seriesId, "Recent Series", "Series", 2025, actorKey, "Guest"),
                    CreateDisplayItem(Guid.NewGuid(), Guid.NewGuid(), "Older Movie", "Movie", 2024, actorKey, "Supporting")
                ],
                CancellationToken.None);
            await store.ActivateRebuildAsync(generation, DateTimeOffset.UtcNow, CancellationToken.None);

            var page = await store.QueryFilmographyAsync(
                actorKey,
                new FilmographyQuery(0, 1, "Actor", []),
                null,
                CancellationToken.None);

            Assert.Equal(2, page.TotalRecordCount);
            var item = Assert.Single(page.Items);
            Assert.Equal(seriesId, item.ItemId);
            Assert.Equal("Recent Series", item.ItemName);
            Assert.Contains("Lead", item.Role, StringComparison.Ordinal);
            Assert.Contains("Guest", item.Role, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ActivatingRebuildRemovesSupersededGenerations()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "actors-index.db");

        try
        {
            await using var store = new SqliteActorsIndexStore(databasePath);
            await store.InitializeAsync(CancellationToken.None);
            var firstGeneration = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            await store.ActivateRebuildAsync(firstGeneration, DateTimeOffset.UtcNow, CancellationToken.None);
            _ = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            var finalGeneration = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            await store.ActivateRebuildAsync(finalGeneration, DateTimeOffset.UtcNow, CancellationToken.None);

            using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync(CancellationToken.None);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM index_generations;";

            Assert.Equal(1L, (long)(await command.ExecuteScalarAsync(CancellationToken.None))!);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RebuildActivationStoresIndependentIncrementalWatermark()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            await store.InitializeAsync(CancellationToken.None);
            var generation = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            var watermark = new DateTimeOffset(2026, 7, 17, 2, 55, 0, TimeSpan.Zero);
            var completed = watermark.AddMinutes(10);

            await store.ActivateRebuildAsync(generation, completed, watermark, CancellationToken.None);

            Assert.Equal(watermark, await store.GetIncrementalWatermarkAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IndexedMediaItem CreateMovie(
        Guid sourceItemId,
        string name,
        string actorKey,
        string actorName,
        IReadOnlyCollection<Guid>? libraryIds = null)
    {
        return new IndexedMediaItem(
            sourceItemId,
            sourceItemId,
            name,
            "Movie",
            2025,
            DateTimeOffset.UtcNow,
            libraryIds ?? [],
            [new IndexedCredit(actorKey, null, actorName, null, "Actor")]);
    }

    private static async Task<IReadOnlyList<string>> GetTablesWithPlannerStatisticsAsync(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync(CancellationToken.None);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT tbl FROM sqlite_stat1 WHERE tbl IN ('credits', 'media_items') GROUP BY tbl ORDER BY tbl;";
        using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var tables = new List<string>();
        while (await reader.ReadAsync(CancellationToken.None))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static IndexedMediaItem CreateDisplayItem(
        Guid sourceItemId,
        Guid displayItemId,
        string name,
        string itemType,
        int year,
        string actorKey,
        string role)
    {
        return new IndexedMediaItem(
            sourceItemId,
            displayItemId,
            name,
            itemType,
            year,
            DateTimeOffset.UtcNow,
            [],
            [new IndexedCredit(actorKey, null, "Filmography Actor", role, "Actor")]);
    }

    private sealed class InvalidSchemaResourceProvider : ISqlResourceProvider
    {
        private readonly ISqlResourceProvider _inner;

        public InvalidSchemaResourceProvider(ISqlResourceProvider inner)
        {
            _inner = inner;
            SchemaResources = inner.SchemaResources
                .Select(resource => resource.Name == "credits.sql"
                    ? resource with { Content = "THIS IS NOT VALID SQL;" }
                    : resource)
                .ToArray();
            SchemaHash = EmbeddedSqlResourceProvider.CalculateSchemaHash(SchemaResources);
        }

        public IReadOnlyList<SqlResource> SchemaResources { get; }

        public string SchemaHash { get; }

        public string GetQuery(string fileName)
        {
            return _inner.GetQuery(fileName);
        }
    }
}
