using Avala.Agents.Contracts.Events;
using Avala.Sdk;

namespace Avala.Agents.Contracts.Sessions;

public sealed record PermissionDecision(ItemId Item, PermissionAnswer Answer)
{
    public Option<string> Message { get; init; }
}
