using Avala.Sdk;

namespace Avala.Fleet.Contracts;

public interface IFleetActivity
{
    FleetZone Zone { get; }

    ValueTask<FleetUsageView> UsageAsync(FleetUsageQuery query, CancellationToken cancellationToken);

    ValueTask<FleetJobsView> JobsAsync(string repository, CancellationToken cancellationToken);
}

public enum FleetReadState
{
    NotConnected,
    Paused,
    Unreachable,
    KeyRefused,
    Outdated,
    Refused,
    Read,
}

public sealed record FleetTokens(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite, decimal Reasoning);

public sealed record FleetCost(string Currency, string Basis, decimal Amount);

public sealed record FleetZone(string Id, bool InUtc);

public enum FleetStep
{
    Day,
    Week,
    Month,
}

public sealed record FleetUsageQuery(DateOnly From, DateOnly To, FleetStep Step, Option<string> Repository);

public sealed record FleetCostKind(string Currency, string Basis);

public sealed record FleetTotals(FleetTokens Tokens, IReadOnlyList<FleetCost> Costs, int Jobs, int Instances);

public sealed record FleetPeriod(DateOnly Start, FleetTokens Tokens, IReadOnlyList<FleetCost> Costs);

public enum FleetShareKind
{
    Repository,
    Unknown,
    OutsideAnyJob,
    Instance,
    Model,
}

public sealed record FleetShare(FleetShareKind Kind, string Key, string Name, FleetTokens Tokens, IReadOnlyList<FleetCost> Costs, int Jobs);

public sealed record FleetUsage(
    Option<FleetCostKind> Reference,
    FleetTotals Totals,
    IReadOnlyList<FleetPeriod> Series,
    IReadOnlyList<FleetShare> Repositories,
    IReadOnlyList<FleetShare> Instances,
    IReadOnlyList<FleetShare> Models)
{
    public static FleetUsage Empty { get; } =
        new(Option<FleetCostKind>.None, new FleetTotals(new FleetTokens(0, 0, 0, 0, 0), [], 0, 0), [], [], [], []);
}

public sealed record FleetUsageView(FleetReadState State, FleetUsage Usage)
{
    public static FleetUsageView Of(FleetReadState state) => new(state, FleetUsage.Empty);
}

public sealed record FleetJob(
    Guid Instance,
    string InstanceName,
    Guid Job,
    string Status,
    string Hold,
    int Attempts,
    string LastOutcome,
    FleetTokens Tokens,
    IReadOnlyList<FleetCost> Costs,
    DateTimeOffset LastActivityAt);

public sealed record FleetJobsView(FleetReadState State, IReadOnlyList<FleetJob> Jobs)
{
    public static FleetJobsView Of(FleetReadState state) => new(state, []);
}
