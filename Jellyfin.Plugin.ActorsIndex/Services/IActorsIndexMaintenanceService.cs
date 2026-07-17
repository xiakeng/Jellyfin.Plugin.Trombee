using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Trombee.Services;

/// <summary>
/// Maintains the durable Trombee actors index.
/// </summary>
public interface IActorsIndexMaintenanceService
{
    /// <summary>
    /// Rebuilds every actor-index row and atomically activates the result.
    /// </summary>
    /// <param name="progress">Optional task progress.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task representing the operation.</returns>
    Task FullRebuildAsync(IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Applies modified items and reconciles deletions since the prior successful run.
    /// </summary>
    /// <param name="progress">Optional task progress.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task representing the operation.</returns>
    Task IncrementalUpdateAsync(IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a live batch of Jellyfin library changes.
    /// </summary>
    /// <param name="changes">The normalized source-item changes.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task representing the operation.</returns>
    Task ProcessChangesAsync(IReadOnlyCollection<LibraryChange> changes, CancellationToken cancellationToken);
}
