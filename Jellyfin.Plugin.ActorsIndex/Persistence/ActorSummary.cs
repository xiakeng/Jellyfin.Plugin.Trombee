using System;

namespace Jellyfin.Plugin.Trombee.Persistence;

/// <summary>
/// A compact actor result returned by the paged index endpoint.
/// </summary>
/// <param name="ActorKey">The stable actor key used by the filmography endpoint.</param>
/// <param name="Name">The actor display name.</param>
/// <param name="Appearances">The number of distinct displayed items.</param>
/// <param name="PersonId">The Jellyfin person item identifier, when available.</param>
public sealed record ActorSummary(string ActorKey, string Name, int Appearances, Guid? PersonId);
