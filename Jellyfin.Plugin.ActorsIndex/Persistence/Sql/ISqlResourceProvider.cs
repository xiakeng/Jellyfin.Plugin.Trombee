using System.Collections.Generic;

namespace Jellyfin.Plugin.Trombee.Persistence.Sql;

/// <summary>
/// Provides embedded schema and query SQL.
/// </summary>
public interface ISqlResourceProvider
{
    /// <summary>
    /// Gets schema resources in deterministic execution order.
    /// </summary>
    IReadOnlyList<SqlResource> SchemaResources { get; }

    /// <summary>
    /// Gets the composite SHA-256 hash of all schema resources.
    /// </summary>
    string SchemaHash { get; }

    /// <summary>
    /// Gets one complete query by filename.
    /// </summary>
    /// <param name="fileName">The query resource filename.</param>
    /// <returns>The query SQL.</returns>
    string GetQuery(string fileName);
}
