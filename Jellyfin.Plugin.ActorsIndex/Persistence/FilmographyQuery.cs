using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Trombee.Persistence;

/// <summary>
/// Parameters for a server-paged filmography query.
/// </summary>
/// <param name="StartIndex">The zero-based result offset.</param>
/// <param name="Limit">The maximum number of rows to return.</param>
/// <param name="PersonType">The Jellyfin person type.</param>
/// <param name="LibraryIds">Optional top-level library filters.</param>
public sealed record FilmographyQuery(
    int StartIndex,
    int Limit,
    string PersonType,
    IReadOnlyCollection<Guid> LibraryIds);
