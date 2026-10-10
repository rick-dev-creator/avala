using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Delegation.Contracts;

public enum DelegationError
{
    Unreadable,
    TooLarge,
    Malformed,
    UnknownField,
    InvalidConnections,
    UnknownRouting,
    InvalidDepth,
    InvalidChildren,
    MalformedInput,
    NoJob,
    NotDeclared,
    DepthExceeded,
    TooManyChildren,
    AutonomyLoosened,
    NotSubmitted,
    UnknownEscalation,
    InvalidWindow,
    UnknownRole,
    RoleLoosened,
    UnofferedModel,
    UnofferedEffort,
}

public enum ChildOutcome
{
    Integrated,
    Conflict,
    NotIntegrated,
    Held,
    RetriesExhausted,
    Failed,
    Discarded,
    Reported,
}

public enum ChildRole
{
    Worker,
    Reviewer,
    Research,
}

public sealed record ParentEscalation(TimeSpan Window);

public sealed record ChildReport(JobId Child, ChildOutcome Outcome, JobStatus Status, DateTimeOffset At)
{
    public Option<string> Summary { get; init; }

    public IReadOnlyList<FileChange> Files { get; init; } = [];

    public Option<VerificationReport> Verification { get; init; }

    public IReadOnlyList<Cost> Spent { get; init; } = [];

    public long Tokens { get; init; }

    public Option<BudgetCarve> Carve { get; init; }

    public Option<ApprovalDelivery> Delivery { get; init; }

    public IReadOnlyList<string> Conflicts { get; init; } = [];

    public Option<HoldReason> Hold { get; init; }

    public Option<JobRejection> Refusal { get; init; }
}

public sealed record DelegationRecord(SessionId Session, ItemId Item, string Instruction, DateTimeOffset At)
{
    public Option<JobId> Parent { get; init; }

    public int Depth { get; init; }

    public Option<JobId> Child { get; init; }

    public Option<ConnectionName> Connection { get; init; }

    public Option<ConnectionChoice> Choice { get; init; }

    public Option<Autonomy> Autonomy { get; init; }

    public ChildRole Role { get; init; }

    public Option<ParentEscalation> Escalation { get; init; }

    public bool ReadOnly => Role != ChildRole.Worker;

    public Option<DelegationError> Refusal { get; init; }

    public Option<JobRejection> Rejection { get; init; }

    public Option<ChildReport> Report { get; init; }

    public Option<CallAnswer> Answered { get; init; }
}

public enum AnswerRoute
{
    ToolResult,
    Message,
}

public sealed record CallAnswer(AnswerRoute Route, DateTimeOffset At);

public sealed record ChildDelegated(DelegationRecord Delegation) : IIntegrationEvent;

public sealed record ReportDelivered(DelegationRecord Delegation) : IIntegrationEvent;

public sealed record DelegationRefused(DelegationRecord Delegation) : IIntegrationEvent;

public sealed record ChildReported(DelegationRecord Delegation) : IIntegrationEvent;

public sealed record ParentAsked(DelegationRecord Delegation, SessionId Session, ItemId Item, string Asking, DateTimeOffset Until) : IIntegrationEvent
{
    public string Note { get; init; } = string.Empty;

    public bool InCall { get; init; }
}
