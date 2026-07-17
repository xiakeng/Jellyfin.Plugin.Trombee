using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Trombee.Persistence;
using Jellyfin.Plugin.Trombee.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Trombee.Tests.Services;

public sealed class JellyfinActorsIndexSourceTests
{
    [Fact]
    public async Task ItemsAreReadFromJellyfinInBoundedPages()
    {
        var items = Enumerable.Range(0, 201)
            .Select(index => (BaseItem)new MediaBrowser.Controller.Entities.Movies.Movie
            {
                Id = Guid.NewGuid(),
                Name = $"Movie {index}"
            })
            .ToArray();
        var queries = new List<InternalItemsQuery>();
        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager
            .Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns((InternalItemsQuery query) =>
            {
                queries.Add(query);
                var startIndex = query.StartIndex ?? 0;
                var limit = query.Limit ?? items.Length;
                return items.Skip(startIndex).Take(limit).ToArray();
            });
        libraryManager
            .Setup(manager => manager.GetCollectionFolders(It.IsAny<BaseItem>()))
            .Returns([]);
        libraryManager
            .Setup(manager => manager.GetPeople(It.IsAny<BaseItem>()))
            .Returns([]);
        var source = new JellyfinActorsIndexSource(libraryManager.Object);

        var results = new List<IndexedMediaItem>();
        await foreach (var item in source.GetItemsAsync(null, CancellationToken.None))
        {
            results.Add(item);
        }

        Assert.Equal(201, results.Count);
        Assert.Equal([0, 200], queries.Select(query => query.StartIndex ?? 0));
        Assert.All(queries, query => Assert.Equal(200, query.Limit));
    }

    [Fact]
    public async Task EpisodeUsesSeriesAsDisplayItemAndPersonIdAsActorKey()
    {
        var episodeId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var episode = new Episode
        {
            Id = episodeId,
            Name = "Episode One",
            SeriesId = seriesId,
            SeriesName = "Fallback Series"
        };
        var series = new Series
        {
            Id = seriesId,
            Name = "Indexed Series",
            ProductionYear = 2025
        };
        var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
        libraryManager
            .Setup(manager => manager.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns([episode]);
        libraryManager
            .Setup(manager => manager.GetItemById(seriesId))
            .Returns(series);
        libraryManager
            .Setup(manager => manager.GetCollectionFolders(episode))
            .Returns([]);
        libraryManager
            .Setup(manager => manager.GetPeople(episode))
            .Returns(
            [
                new PersonInfo
                {
                    Id = personId,
                    Name = "Jane Doe",
                    Role = "Lead",
                    Type = PersonKind.Actor
                }
            ]);
        var source = new JellyfinActorsIndexSource(libraryManager.Object);

        var results = new List<IndexedMediaItem>();
        await foreach (var item in source.GetItemsAsync(null, CancellationToken.None))
        {
            results.Add(item);
        }

        var indexedItem = Assert.Single(results);
        Assert.Equal(episodeId, indexedItem.SourceItemId);
        Assert.Equal(seriesId, indexedItem.DisplayItemId);
        Assert.Equal("Indexed Series", indexedItem.Name);
        Assert.Equal(2025, indexedItem.Year);
        var credit = Assert.Single(indexedItem.Credits);
        Assert.Equal(personId.ToString("N"), credit.ActorKey);
        Assert.Equal(personId, credit.PersonId);
    }
}
