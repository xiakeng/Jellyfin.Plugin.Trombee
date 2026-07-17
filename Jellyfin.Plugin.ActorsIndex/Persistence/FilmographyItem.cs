using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Trombee.Persistence;

/// <summary>
/// A compact filmography item.
/// </summary>
/// <param name="ItemId">The Jellyfin item identifier opened by the frontend.</param>
/// <param name="ItemName">The display name.</param>
/// <param name="Role">The distinct credited roles.</param>
/// <param name="Year">The production year.</param>
/// <param name="ItemType">The display item type.</param>
public sealed record FilmographyItem(
    [property: JsonPropertyName("itemId")] Guid ItemId,
    [property: JsonPropertyName("itemName")] string ItemName,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("year")] int? Year,
    [property: JsonPropertyName("itemType")] string ItemType);
