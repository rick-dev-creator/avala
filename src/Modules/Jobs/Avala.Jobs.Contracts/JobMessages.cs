using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Jobs.Contracts;

public enum JobStatus
{
    Draft,
    Preparing,
    Running,
    Checking,
    NeedsHelp,
    AwaitingReview,
    Approved,
    Discarded,
    Failed,
}

public enum Autonomy
{
    Supervised,
    Autonomous,
}

public enum HoldReason
{
    Stalled,
    SessionLost,
    BudgetExceeded,
    LimitNearlyReached,
    InvalidBudget,
    MemoryExceeded,
    Interrupted,
    Stopped,
    NotResumable,
    InvalidChecks,
    CouldNotStart,
    TurnFailed,
}

public enum SessionHalt
{
    Interrupted,
    Idle,
    Stopped,
    AlreadyClosed,
}

public sealed record JobHold(JobId Job, Option<SessionId> Session, HoldReason Reason, SessionHalt Halt)
{
    public int Attempt { get; init; }

    public Option<UsageLimit> Limit { get; init; }

    public Option<string> Problem { get; init; }
}

public sealed record JobSubmitted(JobId Job) : IIntegrationEvent
{
    public Option<JobId> Parent { get; init; }
}

public sealed record JobHeld(JobHold Hold) : IIntegrationEvent;

public sealed record JobProgressed(JobId Job, JobStatus Status) : IIntegrationEvent
{
    public Option<JobFailure> Failure { get; init; }
}

public sealed record JobSessionStarted(JobId Job, SessionId Session) : IIntegrationEvent
{
    public Option<Autonomy> Autonomy { get; init; }
}

public sealed record JobResumable(JobId Job, SessionId Session) : IIntegrationEvent;

public sealed record JobModelChosen(JobId Job, ModelChoice Model) : IIntegrationEvent;

public sealed record JobRemoteControlChosen(JobId Job, bool On) : IIntegrationEvent;

public sealed record JobSessionRested(JobId Job, SessionId Session) : IIntegrationEvent;
