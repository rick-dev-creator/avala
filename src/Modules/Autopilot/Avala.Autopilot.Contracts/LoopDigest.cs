using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;

namespace Avala.Autopilot.Contracts;

public sealed record LoopDigest(
    LoopState State,
    IReadOnlyList<IterationRecord> Iterations,
    IReadOnlyList<AutoApproval> Approvals,
    IReadOnlyList<BreakerTrip> Breakers,
    IReadOnlyList<LoopPause> Pauses,
    IReadOnlyList<Cost> Spent);

public enum IterationOutcome
{
    ApprovedAutomatically,
    AwaitingReview,
    AwaitingAnswer,
    NeedsHelp,
    Failed,
    NotSubmitted,
    ApprovedByPerson,
    Discarded,
}

public sealed record IterationRecord(int Number, SourcedTask Task, Option<JobId> Job, IterationOutcome Outcome, DateTimeOffset Ended)
{
    public IReadOnlyList<ExceptionReason> Exceptions { get; init; } = [];

    public Option<HoldReason> Hold { get; init; }

    public Option<JobRejection> Rejection { get; init; }

    public Option<FailureSignature> Failure { get; init; }

    public bool ChangedNothing { get; init; }

    public IReadOnlyList<Cost> Cost { get; init; } = [];
}

public enum FailureSource
{
    Check,
    Declaration,
    Hold,
    Job,
    Submission,
}

public sealed record FailureSignature(FailureSource Source, string Detail);

public enum ExceptionReason
{
    NotDeclared,
    UnreadableRules,
    VerificationNotPassed,
    Denial,
    Assumption,
    RuleFileEdited,
    Held,
    ChangesUnknown,
    DeliveryRefused,
}

public sealed record EvidenceSummary(
    int Attempts,
    Option<VerificationOutcome> Verification,
    IReadOnlyList<string> ChecksPassed,
    int PermissionsAllowed,
    int FormsDecided,
    IReadOnlyList<string> FilesChanged);

public sealed record AutoApproval(LoopId Loop, JobId Job, bool Approved, EvidenceSummary Evidence, IReadOnlyList<ExceptionReason> Exceptions, DateTimeOffset At)
{
    public Option<ApprovalDelivery> Delivery { get; init; }

    public Option<JobRejection> Refusal { get; init; }
}

public enum Breaker
{
    Iterations,
    FailuresInARow,
    SameFailure,
    NothingChanged,
    SpendPerLoop,
    SpendPerWindow,
    UsageLimit,
}

public sealed record BreakerTrip(Breaker Breaker, string Subject, decimal Measured, decimal Cap, DateTimeOffset At);

public sealed record LoopPause(PauseReason Reason, DateTimeOffset At, Option<DateTimeOffset> Until)
{
    public Option<string> Window { get; init; }
}
