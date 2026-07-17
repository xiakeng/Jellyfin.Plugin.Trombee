using System;

namespace Jellyfin.Plugin.Trombee.Persistence;

/// <summary>
/// A person credit attached to a Jellyfin source item.
/// </summary>
/// <param name="ActorKey">The stable actor key.</param>
/// <param name="PersonId">The Jellyfin person item identifier, when available.</param>
/// <param name="Name">The display name.</param>
/// <param name="Role">The credited role.</param>
/// <param name="PersonType">The Jellyfin person type.</param>
public sealed record IndexedCredit(string ActorKey, Guid? PersonId, string Name, string? Role, string PersonType);
