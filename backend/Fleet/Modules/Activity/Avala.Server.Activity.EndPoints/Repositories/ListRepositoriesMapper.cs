using Avala.Server.Activity.Contracts.Repositories;

namespace Avala.Server.Activity.EndPoints.Repositories;

internal static class ListRepositoriesMapper
{
    public static ListRepositories ToQuery() => new();

    public static ListRepositoriesResponse ToResponse(FleetRepositories repositories) =>
        new(
            [
                .. repositories.Repositories.Select(repository => new RepositoryItem(
                    repository.Key,
                    repository.Jobs,
                    repository.Instances,
                    Spending.Tokens(repository.Tokens),
                    Spending.Costs(repository.Costs),
                    repository.FirstActivityAt,
                    repository.LastActivityAt)),
            ],
            repositories.UnattributedJobs);
}
