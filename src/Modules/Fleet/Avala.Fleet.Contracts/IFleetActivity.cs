namespace Avala.Fleet.Contracts;

public interface IFleetActivity
{
    ValueTask<FleetView> RepositoriesAsync(CancellationToken cancellationToken);

    ValueTask<FleetJobsView> JobsAsync(string repository, CancellationToken cancellationToken);
}

public enum FleetReadState
{
    NotConnected,
    Paused,
    Unreachable,
    KeyRefused,
    Read,
}

public sealed record FleetTokens(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite, decimal Reasoning);

public sealed record FleetCost(string Currency, string Basis, decimal Amount);

public sealed record FleetRepository(
    string Key,
    int Jobs,
    int Instances,
    FleetTokens Tokens,
    IReadOnlyList<FleetCost> Costs,
    DateTimeOffset FirstActivityAt,
    DateTimeOffset LastActivityAt);

public sealed record FleetView(FleetReadState State, IReadOnlyList<FleetRepository> Repositories, int UnattributedJobs)
{
    public static FleetView Of(FleetReadState state) => new(state, [], 0);
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
