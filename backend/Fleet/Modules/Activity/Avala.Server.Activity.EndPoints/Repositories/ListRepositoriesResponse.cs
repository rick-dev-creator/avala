namespace Avala.Server.Activity.EndPoints.Repositories;

internal sealed record ListRepositoriesResponse(IReadOnlyList<RepositoryItem> Repositories, int UnattributedJobs);

internal sealed record RepositoryItem(
    string Key,
    int Jobs,
    int Instances,
    TokensResponse Tokens,
    IReadOnlyList<CostResponse> Costs,
    DateTimeOffset FirstActivityAt,
    DateTimeOffset LastActivityAt);
