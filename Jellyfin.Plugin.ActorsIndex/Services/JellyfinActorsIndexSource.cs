using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Trombee.Persistence;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Trombee.Services;

/// <summary>
/// Reads actor credits and indexable items from Jellyfin's library database.
/// </summary>
public sealed class JellyfinActorsIndexSource : IActorsIndexSource
{
    private const int SourcePageSize = 200;

    private static readonly BaseItemKind[] _indexableItemTypes =
    [
        BaseItemKind.Movie,
        BaseItemKind.Series,
        BaseItemKind.Episode
    ];

    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinActorsIndexSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    public JellyfinActorsIndexSource(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public Task<int> GetIndexableItemCountAsync(
        DateTimeOffset? modifiedSinceUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_libraryManager.GetItemIds(CreateQuery(modifiedSinceUtc)).Count);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IndexedMediaItem> GetItemsAsync(
        DateTimeOffset? modifiedSinceUtc,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        var startIndex = 0;
        while (true)
        {
            var query = CreateQuery(modifiedSinceUtc);
            query.StartIndex = startIndex;
            query.Limit = SourcePageSize;
            var items = _libraryManager.GetItemList(query);
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var indexedItem = CreateIndexedItem(item);
                if (indexedItem is not null)
                {
                    yield return indexedItem;
                }
            }

            if (items.Count < SourcePageSize)
            {
                yield break;
            }

            startIndex += items.Count;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<Guid>> GetCurrentSourceItemIdsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyCollection<Guid>>(
            _libraryManager.GetItemIds(CreateQuery(null)).ToArray());
    }

    /// <inheritdoc />
    public Task<IndexedMediaItem?> GetItemAsync(Guid sourceItemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = _libraryManager.GetItemById(sourceItemId);
        return Task.FromResult(item is null ? null : CreateIndexedItem(item));
    }

    private static InternalItemsQuery CreateQuery(DateTimeOffset? modifiedSinceUtc)
    {
        return new InternalItemsQuery
        {
            IncludeItemTypes = _indexableItemTypes,
            Recursive = true,
            MinDateLastSaved = modifiedSinceUtc?.UtcDateTime
        };
    }

    private static string CreateActorKey(PersonInfo person)
    {
        if (person.Id != Guid.Empty)
        {
            return person.Id.ToString("N", CultureInfo.InvariantCulture);
        }

        var normalizedName = person.Name!
            .Trim()
            .Normalize(NormalizationForm.FormKC)
            .ToUpperInvariant();
        return "name-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedName))).ToLowerInvariant();
    }

    private IndexedMediaItem? CreateIndexedItem(BaseItem item)
    {
        if (item is not Movie && item is not Series && item is not Episode)
        {
            return null;
        }

        var displayItemId = item.Id;
        var displayName = item.Name;
        var displayYear = item.ProductionYear;
        var displayItemType = item is Series or Episode ? "Series" : "Movie";
        if (item is Episode episode && episode.SeriesId != Guid.Empty)
        {
            displayItemId = episode.SeriesId;
            var series = _libraryManager.GetItemById(episode.SeriesId);
            displayName = series?.Name ?? episode.SeriesName ?? item.Name;
            displayYear = series?.ProductionYear ?? item.ProductionYear;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var libraryIds = _libraryManager
            .GetCollectionFolders(item)
            .Select(folder => folder.Id)
            .Distinct()
            .ToArray();
        var credits = _libraryManager
            .GetPeople(item)
            .Where(person => !string.IsNullOrWhiteSpace(person.Name))
            .Select(CreateIndexedCredit)
            .ToArray();

        return new IndexedMediaItem(
            item.Id,
            displayItemId,
            displayName,
            displayItemType,
            displayYear,
            new DateTimeOffset(item.DateLastSaved.ToUniversalTime()),
            libraryIds,
            credits);
    }

    private IndexedCredit CreateIndexedCredit(PersonInfo person)
    {
        var personItem = _libraryManager.GetPerson(person.Name!);
        Guid? personItemId = personItem?.Id;
        if (personItemId == Guid.Empty)
        {
            personItemId = null;
        }

        return new IndexedCredit(
            personItemId?.ToString("N", CultureInfo.InvariantCulture) ?? CreateActorKey(person),
            personItemId,
            person.Name!,
            person.Role,
            person.Type.ToString());
    }
}
