using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
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

    public Option<ChildRole> Role { get; init; }

    public RoleSettings Settings { get; init; } = RoleSettings.Undeclared;

    public Option<DelegationError> Refuses(int depth, int running, Option<Autonomy> asked, Autonomy granted) =>
        depth > MaxDepth ? DelegationError.DepthExceeded
        : running >= MaxChildren ? DelegationError.TooManyChildren
        : asked == Option<Autonomy>.Some(Autonomy.Autonomous) && granted != Autonomy.Autonomous ? DelegationError.AutonomyLoosened
        : Option<DelegationError>.None;

    public Option<DelegationError> RefusesConnection(Option<ConnectionName> asked) =>
        asked.Match(named => Connections.Contains(named) ? Option<DelegationError>.None : DelegationError.UndeclaredConnection, () => Option<DelegationError>.None);

    public ChildRole RoleOf(Option<ChildRole> asked, ChildRole caller) => asked.Match(chosen => chosen, () => Floor(caller));

    public Option<DelegationError> RefusesRole(Option<ChildRole> asked, ChildRole caller) =>
        asked == Option<ChildRole>.Some(ChildRole.Worker) && Floor(caller) != ChildRole.Worker ? DelegationError.RoleLoosened : Option<DelegationError>.None;

    private ChildRole Floor(ChildRole caller) => caller != ChildRole.Worker ? caller : Role.Match(declared => declared, () => ChildRole.Worker);

    public Option<ConnectionName> InTurn(int earlierChildren) =>
        Connections.Count == 0 ? Option<ConnectionName>.None : Connections[earlierChildren % Connections.Count];
}

internal sealed record RoleSettings(ModelChoice Worker, ModelChoice Reviewer, ModelChoice Research)
{
    public const long SmallestWindow = 20_000;
    public const long LargestWindow = 1_000_000;

    public static RoleSettings Undeclared { get; } = new(ModelChoice.Default, ModelChoice.Default, ModelChoice.Default);

    public ModelChoice Of(ChildRole role) => role switch
    {
        ChildRole.Reviewer => Reviewer,
        ChildRole.Research => Research,
        _ => Worker,
    };

    public RoleSettings With(ChildRole role, ModelChoice choice) => role switch
    {
        ChildRole.Reviewer => this with { Reviewer = choice },
        ChildRole.Research => this with { Research = choice },
        _ => this with { Worker = choice },
    };
}

internal static class Roles
{
    public static Option<ChildRole> Named(string name) => name switch
    {
        "worker" => ChildRole.Worker,
        "reviewer" => ChildRole.Reviewer,
        "research" => ChildRole.Research,
        _ => Option<ChildRole>.None,
    };
}
