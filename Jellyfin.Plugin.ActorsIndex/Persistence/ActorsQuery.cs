using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Trombee.Persistence;

/// <summary>
/// Parameters for a server-paged actor query.
/// </summary>
/// <param name="StartIndex">The zero-based result offset.</param>
/// <param name="Limit">The maximum number of rows to return.</param>
/// <param name="SearchTerm">An optional case-insensitive name search.</param>
/// <param name="SortBy">The sort field.</param>
/// <param name="SortDirection">The sort direction.</param>
/// <param name="PersonType">The Jellyfin person type.</param>
/// <param name="LibraryIds">Optional top-level library filters.</param>
/// <param name="MinimumAppearances">The minimum distinct appearance count.</param>
public sealed record ActorsQuery(
    int StartIndex,
    int Limit,
    string? SearchTerm,
    ActorSortBy SortBy,
    SortDirection SortDirection,
    string PersonType,
    IReadOnlyCollection<Guid> LibraryIds,
    int MinimumAppearances);
