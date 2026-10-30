using Ardalis.Result;
using Mediator;

namespace Avala.Server.Activity.Contracts.Repositories;

internal sealed record ListRepositories : IQuery<Result<FleetRepositories>>;

internal sealed record FleetRepositories(IReadOnlyList<RepositoryActivity> Repositories, int UnattributedJobs);

internal sealed record RepositoryActivity(
    string Key,
    int Jobs,
    int Instances,
    TokenSums Tokens,
    IReadOnlyList<CostSum> Costs,
    DateTimeOffset FirstActivityAt,
    DateTimeOffset LastActivityAt);
