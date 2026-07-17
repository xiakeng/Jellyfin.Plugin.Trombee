using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Trombee.Persistence;

/// <summary>
/// Describes the durable actors-index state.
/// </summary>
/// <param name="HasActiveGeneration">Whether a completed index generation is available.</param>
/// <param name="ActiveGenerationId">The active generation identifier, when available.</param>
/// <param name="LastIncrementalWatermarkUtc">The last successful incremental maintenance watermark.</param>
public sealed record ActorsIndexStatus(
    [property: JsonPropertyName("hasActiveGeneration")] bool HasActiveGeneration,
    [property: JsonPropertyName("activeGenerationId")] long? ActiveGenerationId,
    [property: JsonPropertyName("lastIncrementalWatermarkUtc")] DateTimeOffset? LastIncrementalWatermarkUtc);
