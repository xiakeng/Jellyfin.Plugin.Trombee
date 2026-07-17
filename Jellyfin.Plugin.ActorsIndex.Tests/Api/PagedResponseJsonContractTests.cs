using System.Text.Json;
using Jellyfin.Plugin.Trombee.Persistence;
using Xunit;

namespace Jellyfin.Plugin.Trombee.Tests.Api;

public sealed class PagedResponseJsonContractTests
{
    [Fact]
    public void ActorsPageUsesFrontendCamelCaseProperties()
    {
        var personId = Guid.NewGuid();
        var page = new ActorsPage(
            1,
            0,
            60,
            [new ActorSummary("actor-key", "Actor Name", 3, personId)]);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(page));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("totalRecordCount").GetInt32());
        Assert.Equal(0, root.GetProperty("startIndex").GetInt32());
        Assert.Equal(60, root.GetProperty("limit").GetInt32());
        var actor = root.GetProperty("actors")[0];
        Assert.Equal("actor-key", actor.GetProperty("actorKey").GetString());
        Assert.Equal("Actor Name", actor.GetProperty("name").GetString());
        Assert.Equal(3, actor.GetProperty("appearances").GetInt32());
        Assert.Equal(personId, actor.GetProperty("personId").GetGuid());
    }

    [Fact]
    public void FilmographyPageUsesFrontendCamelCaseProperties()
    {
        var itemId = Guid.NewGuid();
        var page = new FilmographyPage(
            1,
            0,
            60,
            [new FilmographyItem(itemId, "Film Name", "Role", 2026, "Movie")]);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(page));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("totalRecordCount").GetInt32());
        Assert.Equal(0, root.GetProperty("startIndex").GetInt32());
        Assert.Equal(60, root.GetProperty("limit").GetInt32());
        var item = root.GetProperty("items")[0];
        Assert.Equal(itemId, item.GetProperty("itemId").GetGuid());
        Assert.Equal("Film Name", item.GetProperty("itemName").GetString());
        Assert.Equal("Role", item.GetProperty("role").GetString());
        Assert.Equal(2026, item.GetProperty("year").GetInt32());
        Assert.Equal("Movie", item.GetProperty("itemType").GetString());
    }

    [Fact]
    public void ActorsIndexStatusUsesCamelCaseProperties()
    {
        var watermark = DateTimeOffset.UtcNow;
        var status = new ActorsIndexStatus(true, 7, watermark);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(status));
        var root = document.RootElement;
        Assert.True(root.GetProperty("hasActiveGeneration").GetBoolean());
        Assert.Equal(7, root.GetProperty("activeGenerationId").GetInt64());
        Assert.Equal(watermark, root.GetProperty("lastIncrementalWatermarkUtc").GetDateTimeOffset());
    }
}
