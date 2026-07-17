using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Trombee.Services;

/// <summary>
/// Deduplicates live library changes until the debounce window is drained.
/// </summary>
public sealed class LibraryChangeAccumulator
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<Guid, LibraryChangeKind> _changes = [];

    /// <summary>
    /// Records one source-item change. A removal cannot be overwritten by a later update in the same batch.
    /// </summary>
    /// <param name="change">The source-item change.</param>
    public void Record(LibraryChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.SourceItemId == Guid.Empty)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (!_changes.TryGetValue(change.SourceItemId, out var existingKind)
                || change.Kind == LibraryChangeKind.Removed
                || existingKind != LibraryChangeKind.Removed)
            {
                _changes[change.SourceItemId] = change.Kind;
            }
        }
    }

    /// <summary>
    /// Atomically returns and clears the accumulated changes.
    /// </summary>
    /// <returns>The deduplicated changes.</returns>
    public IReadOnlyList<LibraryChange> Drain()
    {
        lock (_syncRoot)
        {
            var result = _changes
                .Select(change => new LibraryChange(change.Key, change.Value))
                .ToArray();
            _changes.Clear();
            return result;
        }
    }
}
