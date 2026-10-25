namespace Avala.Server.Activity.Contracts.Ingestion;

internal sealed record JobSubmittedEvent(Guid Id, DateTimeOffset OccurredAt, Guid Job, string Repository, Guid? Parent, string? Model, string? Effort)
    : IIncomingEvent;

internal sealed record JobModelChosenEvent(Guid Id, DateTimeOffset OccurredAt, Guid Job, string? Model, string? Effort) : IIncomingEvent;

internal sealed record JobProgressedEvent(Guid Id, DateTimeOffset OccurredAt, Guid Job, JobStatus Status) : IIncomingEvent;

internal sealed record JobHeldEvent(Guid Id, DateTimeOffset OccurredAt, Guid Job, HoldReason Reason) : IIncomingEvent;

internal sealed record UsageReportedEvent(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? Job,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    long ReasoningTokens,
    IncomingCost? Cost,
    string? Model) : IIncomingEvent;

internal sealed record IncomingCost(decimal Amount, string Currency, CostBasis Basis);

internal sealed record AttemptVerifiedEvent(Guid Id, DateTimeOffset OccurredAt, Guid Job, int Attempt, VerificationOutcome Outcome) : IIncomingEvent;
