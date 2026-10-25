using Avala.Server.Activity.Contracts;

namespace Avala.Server.Activity.EndPoints.Ingestion.Wire;

internal sealed record JobSubmittedBody(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid Job,
    string Repository,
    Guid? Parent = null,
    string? Model = null,
    string? Effort = null);

internal sealed record JobModelChosenBody(Guid Id, DateTimeOffset OccurredAt, Guid Job, string? Model = null, string? Effort = null);

internal sealed record JobProgressedBody(Guid Id, DateTimeOffset OccurredAt, Guid Job, JobStatus Status);

internal sealed record JobHeldBody(Guid Id, DateTimeOffset OccurredAt, Guid Job, HoldReason Reason);

internal sealed record UsageReportedBody(
    Guid Id,
    DateTimeOffset OccurredAt,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    long ReasoningTokens,
    Guid? Job = null,
    CostBody? Cost = null,
    string? Model = null);

internal sealed record CostBody(decimal Amount, string Currency, CostBasis Basis);

internal sealed record AttemptVerifiedBody(Guid Id, DateTimeOffset OccurredAt, Guid Job, int Attempt, VerificationOutcome Outcome);
