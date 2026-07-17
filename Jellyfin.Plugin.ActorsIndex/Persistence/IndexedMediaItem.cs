using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Trombee.Persistence;

/// <summary>
/// A Jellyfin source item and the credits used by the actors index.
/// </summary>
/// <param name="SourceItemId">The source Movie, Series, or Episode identifier.</param>
/// <param name="DisplayItemId">The item shown in filmography. Episodes use their series identifier.</param>
/// <param name="Name">The display item name.</param>
/// <param name="ItemType">The display item type.</param>
/// <param name="Year">The production year.</param>
/// <param name="DateLastSavedUtc">The Jellyfin last-saved timestamp.</param>
/// <param name="LibraryIds">The top-level libraries containing the source item.</param>
/// <param name="Credits">The person credits attached to the source item.</param>
public sealed record IndexedMediaItem(
    Guid SourceItemId,
    Guid DisplayItemId,
    string Name,
    string ItemType,
    int? Year,
    DateTimeOffset DateLastSavedUtc,
    IReadOnlyCollection<Guid> LibraryIds,
    IReadOnlyCollection<IndexedCredit> Credits);
