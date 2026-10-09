using Avala.Agents.Contracts.Connections;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Policy;

internal enum Routing
{
    Capacity,
    RoundRobin,
}

internal sealed record DelegationRules(IReadOnlyList<ConnectionName> Connections, Routing Routing, int MaxDepth, int MaxChildren)
{
    public const int DefaultDepth = 1;
    public const int DeepestDepth = 8;
    public const int DefaultChildren = 2;
    public const int MostChildren = 16;
    public const int MostConnections = 16;

    public Option<DelegationError> Refuses(int depth, int running, Option<Autonomy> asked, Autonomy granted) =>
        depth > MaxDepth ? DelegationError.DepthExceeded
        : running >= MaxChildren ? DelegationError.TooManyChildren
        : asked == Option<Autonomy>.Some(Autonomy.Autonomous) && granted != Autonomy.Autonomous ? DelegationError.AutonomyLoosened
        : Option<DelegationError>.None;

    public Option<ConnectionName> InTurn(int earlierChildren) =>
        Connections.Count == 0 ? Option<ConnectionName>.None : Connections[earlierChildren % Connections.Count];
}
