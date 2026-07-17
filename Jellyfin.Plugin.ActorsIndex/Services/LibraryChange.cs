using System;

namespace Jellyfin.Plugin.Trombee.Services;

/// <summary>
/// A normalized Jellyfin source-item change.
/// </summary>
/// <param name="SourceItemId">The source item identifier.</param>
/// <param name="Kind">The change kind.</param>
public sealed record LibraryChange(Guid SourceItemId, LibraryChangeKind Kind);
