using Avala.Agents.Contracts.Connections;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Loops;
using Avala.Sdk;
using Avala.Storage;

namespace Avala.Autopilot.Storage;

internal sealed class StoredLoop
{
    public Guid Loop { get; init; }

    public string Repository { get; init; } = string.Empty;

    public long Started { get; init; }

    public string Status { get; set; } = string.Empty;

    public string Fact { get; set; } = string.Empty;

    public static StoredLoop Of(LoopRecord loop) => new()
    {
        Loop = loop.Id.Value,
        Repository = loop.Repository,
        Started = loop.Started.UtcTicks,
        Status = loop.Status.ToString(),
        Fact = StoredJson.Write(LoopFact.From(loop)),
    };

    public LoopRecord Read() => StoredJson.Read<LoopFact>(Fact).Record();

    private sealed record LoopFact(LoopId Id, LoopRequest Request, LoopLimits Limits, DateTimeOffset Started)
    {
        public LoopStatus Status { get; init; }

        public Option<PauseReason> Pause { get; init; }

        public Option<DateTimeOffset> Until { get; init; }

        public Option<LoopEnding> Ending { get; init; }

        public Option<Breaker> Breaker { get; init; }

        public Option<AutopilotError> Error { get; init; }

        public Option<string> Fault { get; init; }

        public Option<Underway> Current { get; init; }

        public Option<ConnectionName> Connection { get; init; }

        public IReadOnlyList<IterationRecord> Iterations { get; init; } = [];

        public IReadOnlyList<AutoApproval> Approvals { get; init; } = [];

        public IReadOnlyList<BreakerTrip> Trips { get; init; } = [];

        public IReadOnlyList<LoopPause> Pauses { get; init; } = [];

        public static LoopFact From(LoopRecord loop) => new(loop.Id, loop.Request, loop.Limits, loop.Started)
        {
            Status = loop.Status,
            Pause = loop.Pause,
            Until = loop.Until,
            Ending = loop.Ending,
            Breaker = loop.Breaker,
            Error = loop.Error,
            Fault = loop.Fault,
            Current = loop.Current,
            Connection = loop.Connection,
            Iterations = loop.Iterations,
            Approvals = loop.Approvals,
            Trips = loop.Trips,
            Pauses = loop.Pauses,
        };

        public LoopRecord Record() => new(Id, Request, Limits, Started)
        {
            Status = Status,
            Pause = Pause,
            Until = Until,
            Ending = Ending,
            Breaker = Breaker,
            Error = Error,
            Fault = Fault,
            Current = Current,
            Connection = Connection,
            Iterations = [.. Iterations],
            Approvals = [.. Approvals],
            Trips = [.. Trips],
            Pauses = [.. Pauses],
        };
    }
}
