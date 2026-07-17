using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.Trombee.Persistence.Sql;

/// <summary>
/// Loads SQL files embedded in the Trombee plugin assembly.
/// </summary>
public sealed class EmbeddedSqlResourceProvider : ISqlResourceProvider
{
    private const string SchemaMarker = ".Persistence.Sql.Schemas.";
    private const string QueryMarker = ".Persistence.Sql.Queries.";

    private readonly Dictionary<string, string> _queries;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmbeddedSqlResourceProvider"/> class.
    /// </summary>
    /// <param name="assembly">The assembly containing embedded SQL files.</param>
    public EmbeddedSqlResourceProvider(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        SchemaResources = LoadResources(assembly, SchemaMarker);
        if (SchemaResources.Count == 0)
        {
            throw new InvalidOperationException("No embedded Trombee schema resources were found.");
        }

        SchemaHash = CalculateSchemaHash(SchemaResources);
        _queries = LoadResources(assembly, QueryMarker)
            .ToDictionary(resource => resource.Name, resource => resource.Content, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public IReadOnlyList<SqlResource> SchemaResources { get; }

    /// <inheritdoc />
    public string SchemaHash { get; }

    /// <summary>
    /// Calculates one deterministic hash for a complete schema resource set.
    /// </summary>
    /// <param name="schemaResources">The schema resources.</param>
    /// <returns>A lowercase SHA-256 hash.</returns>
    public static string CalculateSchemaHash(IEnumerable<SqlResource> schemaResources)
    {
        ArgumentNullException.ThrowIfNull(schemaResources);

        var builder = new StringBuilder();
        foreach (var resource in schemaResources.OrderBy(resource => resource.Name, StringComparer.Ordinal))
        {
            ValidateResource(resource);
            builder.Append(resource.Name)
                .Append('\0')
                .Append(NormalizeLineEndings(resource.Content))
                .Append('\0');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    /// <inheritdoc />
    public string GetQuery(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return _queries.TryGetValue(fileName, out var query)
            ? query
            : throw new InvalidOperationException($"Embedded SQL query '{fileName}' was not found.");
    }

    private static List<SqlResource> LoadResources(Assembly assembly, string marker)
    {
        var resources = new List<SqlResource>();
        foreach (var resourceName in assembly.GetManifestResourceNames()
            .Where(name => name.Contains(marker, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal))
        {
            var markerIndex = resourceName.IndexOf(marker, StringComparison.Ordinal);
            var fileName = resourceName[(markerIndex + marker.Length)..];
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded SQL resource '{resourceName}' could not be opened.");
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var resource = new SqlResource(fileName, reader.ReadToEnd());
            ValidateResource(resource);
            resources.Add(resource);
        }

        var duplicate = resources
            .GroupBy(resource => resource.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate embedded SQL resource filename '{duplicate.Key}'.");
        }

        return resources;
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private static void ValidateResource(SqlResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (string.IsNullOrWhiteSpace(resource.Name) || string.IsNullOrWhiteSpace(resource.Content))
        {
            throw new InvalidOperationException("Embedded SQL resource names and contents must not be empty.");
        }
    }
}
