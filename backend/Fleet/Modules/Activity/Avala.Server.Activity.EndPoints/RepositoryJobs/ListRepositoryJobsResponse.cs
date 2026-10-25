namespace Avala.Server.Activity.EndPoints.RepositoryJobs;

internal sealed record ListRepositoryJobsResponse(IReadOnlyList<RepositoryJobItem> Jobs);

internal sealed record RepositoryJobItem(
    Guid Instance,
    string InstanceName,
    Guid Job,
    string? Status,
    string? Hold,
    int Attempts,
    string? LastOutcome,
    TokensResponse Tokens,
    IReadOnlyList<CostResponse> Costs,
    DateTimeOffset FirstActivityAt,
    DateTimeOffset LastActivityAt);
