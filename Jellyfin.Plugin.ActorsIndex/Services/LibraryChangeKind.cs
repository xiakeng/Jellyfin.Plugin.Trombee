namespace Jellyfin.Plugin.Trombee.Services;

/// <summary>
/// The kind of Jellyfin library change to apply to the actors index.
/// </summary>
public enum LibraryChangeKind
{
    /// <summary>An item was added.</summary>
    Added,

    /// <summary>An item was updated.</summary>
    Updated,

    /// <summary>An item was removed.</summary>
    Removed
}
