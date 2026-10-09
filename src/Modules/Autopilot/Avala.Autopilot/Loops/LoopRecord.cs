using System.Collections.Immutable;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Loops;

internal sealed record Underway(int Iteration, SourcedTask Task, JobId Job)
{
    public bool ContinuesAfterReset { get; init; }
}

internal sealed record LoopRecord(LoopId Id, LoopRequest Request, DateTimeOffset Started)
{
    public LoopStatus Status { get; init; } = LoopStatus.Running;

    public Option<PauseReason> Pause { get; init; }

    public Option<DateTimeOffset> Until { get; init; }

    public Option<LoopEnding> Ending { get; init; }

    public Option<Breaker> Breaker { get; init; }

    public Option<AutopilotError> Error { get; init; }

    public Option<Underway> Current { get; init; }

    public Option<ConnectionName> Connection { get; init; }

    public ImmutableList<IterationRecord> Iterations { get; init; } = [];

    public ImmutableList<AutoApproval> Approvals { get; init; } = [];

    public ImmutableList<BreakerTrip> Trips { get; init; } = [];

    public ImmutableList<LoopPause> Pauses { get; init; } = [];

    public string Repository => Request.Repository;

    public int NextIteration => Iterations.Count + 1;

    public IReadOnlyList<Cost> Spent =>
        [.. Iterations.SelectMany(iteration => iteration.Cost).GroupBy(cost => cost.Currency, StringComparer.Ordinal).Select(spent => new Cost(spent.Sum(cost => cost.Amount), spent.Key))];

    public LoopState State => new(Id, Repository, Status, Started, Iterations.Count, Current.Map(underway => underway.Job))
    {
        Until = Until,
        Pause = Pause,
        Ending = Ending,
        Breaker = Breaker,
        Error = Error,
    };

    public LoopDigest Digest => new(State, Iterations, Approvals, Trips, Pauses, Spent);

    public static LoopRecord Begin(LoopId id, LoopRequest request, DateTimeOffset started) =>
        new(id, request, started) { Connection = request.Connection };

    public LoopRecord Took(SourcedTask task, JobId job) => this with { Current = new Underway(NextIteration, task, job) };

    public LoopRecord Awaiting(Func<Underway, Underway> change) => this with { Current = Current.Map(change) };

    public LoopRecord Settled(IterationRecord iteration, Option<ConnectionName> connection) => this with
    {
        Current = Option<Underway>.None,
        Iterations = Iterations.Add(iteration),
        Connection = connection.IsSome ? connection : Connection,
    };

    public LoopRecord Approved(AutoApproval approval) => this with { Approvals = Approvals.Add(approval) };

    public LoopRecord Paused(LoopPause pause) => this with
    {
        Status = LoopStatus.Paused,
        Pause = pause.Reason,
        Until = pause.Until,
        Pauses = Pauses.Add(pause),
    };

    public LoopRecord Waiting(DateTimeOffset until) => this with { Status = LoopStatus.Waiting, Until = until };

    public LoopRecord Resumed() => this with { Status = LoopStatus.Running, Pause = Option<PauseReason>.None, Until = Option<DateTimeOffset>.None };

    public LoopRecord Ended(LoopEnding ending) => this with
    {
        Status = LoopStatus.Ended,
        Ending = ending,
        Pause = Option<PauseReason>.None,
        Until = Option<DateTimeOffset>.None,
    };

    public LoopRecord Tripped(BreakerTrip trip) => Ended(LoopEnding.BreakerTripped) with { Breaker = trip.Breaker, Trips = Trips.Add(trip) };

    public LoopRecord Failed(AutopilotError error) => Ended(LoopEnding.SourceFailed) with { Error = error };
}
