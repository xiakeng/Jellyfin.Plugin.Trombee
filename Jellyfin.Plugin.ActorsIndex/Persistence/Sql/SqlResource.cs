namespace Jellyfin.Plugin.Trombee.Persistence.Sql;

/// <summary>
/// An embedded SQL resource.
/// </summary>
/// <param name="Name">The resource filename.</param>
/// <param name="Content">The SQL content.</param>
public sealed record SqlResource(string Name, string Content);
