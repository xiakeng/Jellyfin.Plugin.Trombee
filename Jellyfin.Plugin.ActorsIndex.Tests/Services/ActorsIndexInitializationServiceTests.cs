using Jellyfin.Plugin.Trombee.Persistence;
using Jellyfin.Plugin.Trombee.Persistence.Sql;
using Jellyfin.Plugin.Trombee.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Trombee.Tests.Services;

public sealed class ActorsIndexInitializationServiceTests
{
    [Fact]
    public async Task StartAsyncCreatesActorsIndexSchema()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            var service = new ActorsIndexInitializationService(
                store,
                NullLogger<ActorsIndexInitializationService>.Instance);

            await service.StartAsync(CancellationToken.None);

            Assert.False(await store.HasActiveGenerationAsync(CancellationToken.None));
            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InitializationFailureDoesNotFailJellyfinStartup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "actors-index.db");

        try
        {
            var embeddedResources = new EmbeddedSqlResourceProvider(typeof(SqliteActorsIndexStore).Assembly);
            await using var store = new SqliteActorsIndexStore(
                databasePath,
                new InvalidSchemaResourceProvider(embeddedResources));
            var service = new ActorsIndexInitializationService(
                store,
                NullLogger<ActorsIndexInitializationService>.Instance);

            await service.StartAsync(CancellationToken.None);

            Assert.False(File.Exists(databasePath));
            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
