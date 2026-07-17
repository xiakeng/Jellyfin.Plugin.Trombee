using Jellyfin.Plugin.Trombee.Persistence;
using Jellyfin.Plugin.Trombee.Persistence.Sql;
using Xunit;

namespace Jellyfin.Plugin.Trombee.Tests.Persistence;

public sealed class EmbeddedSqlResourceProviderTests
{
    [Fact]
    public void LoadsOrderedSchemaResourcesAndCalculatesOneCompositeHash()
    {
        var provider = new EmbeddedSqlResourceProvider(typeof(SqliteActorsIndexStore).Assembly);

        Assert.Equal(
            [
                "credits.sql",
                "index_generations.sql",
                "index_state.sql",
                "media_item_libraries.sql",
                "media_items.sql",
                "schema_metadata.sql"
            ],
            provider.SchemaResources.Select(resource => resource.Name));
        Assert.Equal(64, provider.SchemaHash.Length);
        Assert.All(provider.SchemaHash, character => Assert.True(Uri.IsHexDigit(character)));
    }

    [Fact]
    public void CompositeHashChangesWhenAnySchemaResourceChanges()
    {
        SqlResource[] original =
        [
            new("TableAlpha.sql", "CREATE TABLE alpha (id INTEGER);"),
            new("TableBeta.sql", "CREATE TABLE beta (id INTEGER);")
        ];
        SqlResource[] changed =
        [
            original[0],
            original[1] with { Content = "CREATE TABLE beta (id INTEGER, name TEXT);" }
        ];

        var originalHash = EmbeddedSqlResourceProvider.CalculateSchemaHash(original);
        var changedHash = EmbeddedSqlResourceProvider.CalculateSchemaHash(changed);

        Assert.NotEqual(originalHash, changedHash);
    }

    [Theory]
    [InlineData("QueryActorsCount.sql", "SELECT COUNT(*)")]
    [InlineData("QueryActorsPage.sql", "LIMIT $limit OFFSET $startIndex")]
    [InlineData("QueryFilmographyCount.sql", "SELECT COUNT(*)")]
    [InlineData("QueryFilmographyPage.sql", "LIMIT $limit OFFSET $startIndex")]
    public void LoadsEachComplexQueryFromItsOwnResource(string fileName, string terminalSql)
    {
        var provider = new EmbeddedSqlResourceProvider(typeof(SqliteActorsIndexStore).Assembly);

        var query = provider.GetQuery(fileName);

        Assert.Contains(terminalSql, query, StringComparison.Ordinal);
    }
}
