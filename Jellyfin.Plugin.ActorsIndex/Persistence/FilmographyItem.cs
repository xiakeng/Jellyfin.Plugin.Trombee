using System;

namespace Jellyfin.Plugin.Trombee.Persistence;

/// <summary>
/// A compact filmography item.
/// </summary>
/// <param name="ItemId">The Jellyfin item identifier opened by the frontend.</param>
/// <param name="ItemName">The display name.</param>
/// <param name="Role">The distinct credited roles.</param>
/// <param name="Year">The production year.</param>
/// <param name="ItemType">The display item type.</param>
public sealed record FilmographyItem(Guid ItemId, string ItemName, string Role, int? Year, string ItemType);
