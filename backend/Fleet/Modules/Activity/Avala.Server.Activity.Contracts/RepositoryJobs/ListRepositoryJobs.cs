using Ardalis.Result;
using Mediator;

namespace Avala.Server.Activity.Contracts.RepositoryJobs;

internal sealed record ListRepositoryJobs(string? Key) : IQuery<Result<IReadOnlyList<RepositoryJob>>>;

internal sealed record RepositoryJob(
    Guid Instance,
    string InstanceName,
    Guid Job,
    JobStatus? Status,
    HoldReason? Hold,
    int Attempts,
    VerificationOutcome? LastOutcome,
    TokenSums Tokens,
    IReadOnlyList<CostSum> Costs,
    DateTimeOffset FirstActivityAt,
    DateTimeOffset LastActivityAt);
