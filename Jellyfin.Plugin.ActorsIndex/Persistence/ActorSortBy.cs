namespace Jellyfin.Plugin.Trombee.Persistence;

/// <summary>
/// The supported actor-index sort fields.
/// </summary>
public enum ActorSortBy
{
    /// <summary>Sort by the number of distinct appearances.</summary>
    Appearances,

    /// <summary>Sort by display name.</summary>
    Name
}
