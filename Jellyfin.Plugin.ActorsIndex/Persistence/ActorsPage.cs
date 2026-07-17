using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Trombee.Persistence;

/// <summary>
/// A page of actor summaries.
/// </summary>
/// <param name="TotalRecordCount">The number of matching actors before paging.</param>
/// <param name="StartIndex">The requested zero-based offset.</param>
/// <param name="Limit">The requested page size.</param>
/// <param name="Actors">The actors in the requested page.</param>
public sealed record ActorsPage(
    [property: JsonPropertyName("totalRecordCount")] int TotalRecordCount,
    [property: JsonPropertyName("startIndex")] int StartIndex,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("actors")] IReadOnlyList<ActorSummary> Actors);
