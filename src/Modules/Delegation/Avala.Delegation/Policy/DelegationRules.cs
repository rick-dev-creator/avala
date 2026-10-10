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

    public EscalationTerms Escalation { get; init; } = EscalationTerms.Undeclared;

    public Option<ChildRole> Role { get; init; }

    public Option<DelegationError> Refuses(int depth, int running, Option<Autonomy> asked, Autonomy granted) =>
        depth > MaxDepth ? DelegationError.DepthExceeded
        : running >= MaxChildren ? DelegationError.TooManyChildren
        : asked == Option<Autonomy>.Some(Autonomy.Autonomous) && granted != Autonomy.Autonomous ? DelegationError.AutonomyLoosened
        : Option<DelegationError>.None;

    public ChildRole RoleOf(Option<ChildRole> asked, ChildRole caller) => asked.Match(chosen => chosen, () => Floor(caller));

    public Option<DelegationError> RefusesRole(Option<ChildRole> asked, ChildRole caller) =>
        asked == Option<ChildRole>.Some(ChildRole.Worker) && Floor(caller) != ChildRole.Worker ? DelegationError.RoleLoosened : Option<DelegationError>.None;

    private ChildRole Floor(ChildRole caller) => caller != ChildRole.Worker ? caller : Role.Match(declared => declared, () => ChildRole.Worker);

    public Option<ConnectionName> InTurn(int earlierChildren) =>
        Connections.Count == 0 ? Option<ConnectionName>.None : Connections[earlierChildren % Connections.Count];
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

internal sealed record EscalationTerms(Option<bool> AsksParent, Option<TimeSpan> Window)
{
    public static TimeSpan DefaultWindow { get; } = TimeSpan.FromSeconds(120);

    public static TimeSpan ShortestWindow { get; } = TimeSpan.FromSeconds(10);

    public static TimeSpan LongestWindow { get; } = TimeSpan.FromSeconds(3600);

    public static EscalationTerms Undeclared { get; } = new(Option<bool>.None, Option<TimeSpan>.None);

    public EscalationTerms Over(EscalationTerms machine) =>
        new(AsksParent.IsSome ? AsksParent : machine.AsksParent, Window.IsSome ? Window : machine.Window);

    public Option<ParentEscalation> Kept =>
        AsksParent.Match(asks => asks, () => false)
            ? new ParentEscalation(Window.Match(window => window, () => DefaultWindow))
            : Option<ParentEscalation>.None;
}
