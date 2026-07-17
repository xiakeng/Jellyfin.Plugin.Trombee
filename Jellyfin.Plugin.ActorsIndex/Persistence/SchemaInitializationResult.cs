namespace Jellyfin.Plugin.Trombee.Persistence;

/// <summary>
/// Describes what schema initialization did to the actors-index database.
/// </summary>
public enum SchemaInitializationResult
{
    /// <summary>
    /// The existing database schema matched the embedded schema.
    /// </summary>
    Unchanged,

    /// <summary>
    /// A new database was created.
    /// </summary>
    Created,

    /// <summary>
    /// An incompatible database was replaced.
    /// </summary>
    Recreated
}
