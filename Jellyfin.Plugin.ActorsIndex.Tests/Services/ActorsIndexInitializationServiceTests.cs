using Jellyfin.Plugin.Trombee.Persistence;
using Jellyfin.Plugin.Trombee.Persistence.Sql;
using Jellyfin.Plugin.Trombee.Services;
using Jellyfin.Plugin.Trombee.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Trombee.Tests.Services;

public sealed class ActorsIndexInitializationServiceTests
{
    [Fact]
    public async Task NewDatabaseQueuesFullRebuildAfterJellyfinStarts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var started = new CancellationTokenSource();
        using var stopping = new CancellationTokenSource();
        using var stopped = new CancellationTokenSource();

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            var taskManager = new Mock<ITaskManager>();
            var lifetime = new Mock<IHostApplicationLifetime>();
            lifetime.SetupGet(value => value.ApplicationStarted).Returns(started.Token);
            lifetime.SetupGet(value => value.ApplicationStopping).Returns(stopping.Token);
            lifetime.SetupGet(value => value.ApplicationStopped).Returns(stopped.Token);
            var service = new ActorsIndexInitializationService(
                store,
                taskManager.Object,
                lifetime.Object,
                NullLogger<ActorsIndexInitializationService>.Instance);

            await service.StartAsync(CancellationToken.None);
            taskManager.Verify(
                manager => manager.QueueIfNotRunning<RebuildActorsIndexTask>(),
                Times.Never);

            started.Cancel();

            taskManager.Verify(
                manager => manager.QueueIfNotRunning<RebuildActorsIndexTask>(),
                Times.Once);
            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RecreatedDatabaseQueuesFullRebuildAfterJellyfinStarts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "actors-index.db");
        using var started = new CancellationTokenSource();

        try
        {
            await using (var originalStore = new SqliteActorsIndexStore(databasePath))
            {
                _ = await originalStore.InitializeAsync(CancellationToken.None);
            }

            await SetStoredHashAsync(databasePath, "different");
            await using var store = new SqliteActorsIndexStore(databasePath);
            var taskManager = new Mock<ITaskManager>();
            var service = new ActorsIndexInitializationService(
                store,
                taskManager.Object,
                CreateLifetime(started.Token).Object,
                NullLogger<ActorsIndexInitializationService>.Instance);

            await service.StartAsync(CancellationToken.None);
            started.Cancel();

            taskManager.Verify(
                manager => manager.QueueIfNotRunning<RebuildActorsIndexTask>(),
                Times.Once);
            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MatchingSchemaDoesNotQueueFullRebuild()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "actors-index.db");
        using var started = new CancellationTokenSource();

        try
        {
            await using (var originalStore = new SqliteActorsIndexStore(databasePath))
            {
                _ = await originalStore.InitializeAsync(CancellationToken.None);
            }

            await using var store = new SqliteActorsIndexStore(databasePath);
            var taskManager = new Mock<ITaskManager>();
            var service = new ActorsIndexInitializationService(
                store,
                taskManager.Object,
                CreateLifetime(started.Token).Object,
                NullLogger<ActorsIndexInitializationService>.Instance);

            await service.StartAsync(CancellationToken.None);
            started.Cancel();

            taskManager.Verify(
                manager => manager.QueueIfNotRunning<RebuildActorsIndexTask>(),
                Times.Never);
            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InitializationFailureDoesNotQueueRebuildOrFailJellyfinStartup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var started = new CancellationTokenSource();

        try
        {
            var embeddedResources = new EmbeddedSqlResourceProvider(typeof(SqliteActorsIndexStore).Assembly);
            await using var store = new SqliteActorsIndexStore(
                Path.Combine(directory, "actors-index.db"),
                new InvalidSchemaResourceProvider(embeddedResources));
            var taskManager = new Mock<ITaskManager>();
            var service = new ActorsIndexInitializationService(
                store,
                taskManager.Object,
                CreateLifetime(started.Token).Object,
                NullLogger<ActorsIndexInitializationService>.Instance);

            await service.StartAsync(CancellationToken.None);
            started.Cancel();

            taskManager.Verify(
                manager => manager.QueueIfNotRunning<RebuildActorsIndexTask>(),
                Times.Never);
            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Mock<IHostApplicationLifetime> CreateLifetime(CancellationToken startedToken)
    {
        var lifetime = new Mock<IHostApplicationLifetime>();
        lifetime.SetupGet(value => value.ApplicationStarted).Returns(startedToken);
        lifetime.SetupGet(value => value.ApplicationStopping).Returns(CancellationToken.None);
        lifetime.SetupGet(value => value.ApplicationStopped).Returns(CancellationToken.None);
        return lifetime;
    }

    private static async Task SetStoredHashAsync(string databasePath, string hash)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync(CancellationToken.None);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE schema_metadata SET schema_hash = $schemaHash WHERE singleton_id = 1;";
        command.Parameters.AddWithValue("$schemaHash", hash);
        _ = await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private sealed class InvalidSchemaResourceProvider : ISqlResourceProvider
    {
        private readonly ISqlResourceProvider _inner;

        public InvalidSchemaResourceProvider(ISqlResourceProvider inner)
        {
            _inner = inner;
            SchemaResources = inner.SchemaResources
                .Select(resource => resource.Name == "TableCredits.sql"
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
