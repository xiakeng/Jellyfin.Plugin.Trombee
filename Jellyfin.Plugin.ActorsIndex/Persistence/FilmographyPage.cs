using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Trombee.Persistence;

/// <summary>
/// A page of filmography items.
/// </summary>
/// <param name="TotalRecordCount">The number of matching items before paging.</param>
/// <param name="StartIndex">The requested zero-based offset.</param>
/// <param name="Limit">The requested page size.</param>
/// <param name="Items">The filmography items in the requested page.</param>
public sealed record FilmographyPage(
    [property: JsonPropertyName("totalRecordCount")] int TotalRecordCount,
    [property: JsonPropertyName("startIndex")] int StartIndex,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("items")] IReadOnlyList<FilmographyItem> Items);
