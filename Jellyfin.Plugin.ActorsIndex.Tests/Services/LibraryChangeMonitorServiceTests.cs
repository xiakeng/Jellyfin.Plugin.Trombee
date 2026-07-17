using Jellyfin.Plugin.Trombee.Persistence;
using Jellyfin.Plugin.Trombee.Persistence.Sql;
using Jellyfin.Plugin.Trombee.Services;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Trombee.Tests.Services;

public sealed class LibraryChangeMonitorServiceTests
{
    [Fact]
    public async Task SchemaInitializationFailureDoesNotFailJellyfinStartup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var embeddedResources = new EmbeddedSqlResourceProvider(typeof(SqliteActorsIndexStore).Assembly);
            await using var store = new SqliteActorsIndexStore(
                Path.Combine(directory, "actors-index.db"),
                new InvalidSchemaResourceProvider(embeddedResources));
            var monitor = new LibraryChangeMonitorService(
                Mock.Of<ILibraryManager>(),
                store,
                Mock.Of<IActorsIndexMaintenanceService>(),
                TimeProvider.System,
                NullLogger<LibraryChangeMonitorService>.Instance);

            await monitor.StartAsync(CancellationToken.None);
            await monitor.StopAsync(CancellationToken.None);
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
