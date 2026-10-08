using Avala.Agents.Contracts.Events;

namespace Avala.Agents.Contracts.Sessions;

public sealed record PermissionDecision(ItemId Item, PermissionAnswer Answer);
