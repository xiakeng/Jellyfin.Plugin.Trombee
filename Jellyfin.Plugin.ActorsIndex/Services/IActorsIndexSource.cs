using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trombee.Persistence;

namespace Jellyfin.Plugin.Trombee.Services;

/// <summary>
/// Provides indexable Jellyfin items to the persistence-agnostic maintenance coordinator.
/// </summary>
public interface IActorsIndexSource
{
    /// <summary>
    /// Gets the number of indexable source items for progress reporting.
    /// </summary>
    /// <param name="modifiedSinceUtc">An optional inclusive modified-since boundary.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The number of matching source items.</returns>
    Task<int> GetIndexableItemCountAsync(DateTimeOffset? modifiedSinceUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Enumerates indexable source items.
    /// </summary>
    /// <param name="modifiedSinceUtc">An optional inclusive modified-since boundary.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The matching source items.</returns>
    IAsyncEnumerable<IndexedMediaItem> GetItemsAsync(
        DateTimeOffset? modifiedSinceUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets every currently existing indexable source item identifier.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The current source item identifiers.</returns>
    Task<IReadOnlyCollection<Guid>> GetCurrentSourceItemIdsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets one current source item for live event processing.
    /// </summary>
    /// <param name="sourceItemId">The Jellyfin source item identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The indexed item, or <see langword="null"/> when it no longer exists or is not indexable.</returns>
    Task<IndexedMediaItem?> GetItemAsync(Guid sourceItemId, CancellationToken cancellationToken);
}
