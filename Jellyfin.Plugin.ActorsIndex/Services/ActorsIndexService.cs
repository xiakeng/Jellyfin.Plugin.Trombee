using System;
using System.Collections.Generic;
using System.Linq;
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
/// Provides actors-index data access.
/// </summary>
public class ActorsIndexService
{
    private readonly ILibraryManager _libraryManager;
    private readonly SqliteActorsIndexStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorsIndexService"/> class.
    /// </summary>
    /// <param name="store">The durable actors-index store.</param>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    public ActorsIndexService(SqliteActorsIndexStore store, ILibraryManager libraryManager)
    {
        _store = store;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Returns a server-paged actor index directly from plugin-owned SQLite data.
    /// </summary>
    /// <param name="user">The Jellyfin user used to scope visible source items.</param>
    /// <param name="query">The server-side query parameters.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The matching actor page.</returns>
    public async Task<ActorsPage> GetActorsIndexAsync(
        Jellyfin.Database.Implementations.Entities.User? user,
        ActorsQuery query,
        CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<Guid>? visibleSourceItemIds = user is null
            ? null
            : GetVisibleSourceItemIds(user, query.LibraryIds);
        return await _store.QueryActorsAsync(query, visibleSourceItemIds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a server-paged filmography directly from plugin-owned SQLite data.
    /// </summary>
    /// <param name="user">The Jellyfin user used to scope visible source items.</param>
    /// <param name="actorKey">The stable actor key.</param>
    /// <param name="query">The server-side query parameters.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The matching filmography page.</returns>
    public async Task<FilmographyPage> GetFilmographyAsync(
        Jellyfin.Database.Implementations.Entities.User? user,
        string actorKey,
        FilmographyQuery query,
        CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<Guid>? visibleSourceItemIds = user is null
            ? null
            : GetVisibleSourceItemIds(user, query.LibraryIds);
        return await _store
            .QueryFilmographyAsync(actorKey, query, visibleSourceItemIds, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the durable actors-index status.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The active generation and incremental watermark.</returns>
    public async Task<ActorsIndexStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var generationId = await _store.GetActiveGenerationIdAsync(cancellationToken).ConfigureAwait(false);
        var watermark = await _store.GetIncrementalWatermarkAsync(cancellationToken).ConfigureAwait(false);
        return new ActorsIndexStatus(generationId.HasValue, generationId, watermark);
    }

    /// <summary>
    /// Returns the top-level libraries visible to the given user.
    /// </summary>
    /// <param name="user">The user to scope the results to, or <see langword="null"/> for no scoping.</param>
    /// <returns>A list of libraries with their identifiers and names.</returns>
    public object GetLibraries(Jellyfin.Database.Implementations.Entities.User? user = null)
    {
        var folders = user is not null
            ? _libraryManager.GetUserRootFolder().GetChildren(user, true)
            : _libraryManager.GetUserRootFolder().Children;

        var libraries = folders
            .Where(f => f is Folder)
            .Select(f => new
            {
                id = f.Id.ToString("N", System.Globalization.CultureInfo.InvariantCulture),
                name = f.Name
            })
            .OrderBy(f => f.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new { libraries };
    }

    /// <summary>
    /// Returns simple library statistics.
    /// </summary>
    /// <returns>Counts of root children in the Jellyfin library.</returns>
    public object GetLibraryStats()
    {
        var rootFolder = _libraryManager.RootFolder;
        var directChildren = rootFolder.Children.ToList();

        int libraryCount = directChildren.Count(item => item.IsFolder);
        int movieCount = directChildren.Count(item => item is Movie);
        int seriesCount = directChildren.Count(item => item is Series);

        return new
        {
            status = "ok",
            rootFolderName = rootFolder.Name,
            totalDirectChildren = directChildren.Count,
            libraryFolders = libraryCount,
            movieCount,
            seriesCount,
            sampleChildren = directChildren.Take(5).Select(i => new
            {
                i.Id,
                i.Name,
                itemType = i.GetType().Name
            }).ToArray()
        };
    }

    private IReadOnlyCollection<Guid> GetVisibleSourceItemIds(
        Jellyfin.Database.Implementations.Entities.User user,
        IReadOnlyCollection<Guid> libraryIds)
    {
        if (libraryIds.Count == 0)
        {
            return _libraryManager.GetItemIds(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode },
                Recursive = true
            });
        }

        var result = new HashSet<Guid>();
        foreach (var libraryId in libraryIds)
        {
            result.UnionWith(_libraryManager.GetItemIds(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode },
                Recursive = true,
                ParentId = libraryId
            }));
        }

        return result;
    }
}
