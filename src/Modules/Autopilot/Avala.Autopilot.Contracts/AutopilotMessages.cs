using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Autopilot.Contracts;

public sealed record LoopStarted(LoopState State) : IIntegrationEvent;

public sealed record LoopTaskTaken(LoopId Loop, int Iteration, SourcedTask Task, JobId Job) : IIntegrationEvent;

public sealed record AutoApprovalDecided(AutoApproval Decision) : IIntegrationEvent;

public sealed record LoopIterated(LoopId Loop, IterationRecord Iteration) : IIntegrationEvent;

public sealed record LoopWaiting(LoopId Loop, DateTimeOffset Until) : IIntegrationEvent;

public sealed record LoopPaused(LoopId Loop, LoopPause Pause) : IIntegrationEvent;

public sealed record LoopResumed(LoopId Loop, DateTimeOffset At) : IIntegrationEvent;

public sealed record BreakerTripped(LoopId Loop, BreakerTrip Trip) : IIntegrationEvent;

public sealed record LoopEnded(LoopState State) : IIntegrationEvent;

public enum FollowUpRefusal
{
    MalformedInput,
    NoJob,
    NotAutonomous,
    NotAllowed,
    UnreadableRules,
}

public sealed record FollowUpDecision(SessionId Session, Option<JobId> Job, string Instruction, DateTimeOffset At)
{
    public Option<SourcedTask> Task { get; init; }

    public Option<FollowUpRefusal> Refusal { get; init; }
}

public sealed record FollowUpDecided(FollowUpDecision Decision) : IIntegrationEvent;
