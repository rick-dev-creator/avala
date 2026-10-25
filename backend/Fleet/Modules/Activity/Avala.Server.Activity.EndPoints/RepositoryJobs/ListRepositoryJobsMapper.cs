using Avala.Server.Activity.Contracts.RepositoryJobs;

namespace Avala.Server.Activity.EndPoints.RepositoryJobs;

internal static class ListRepositoryJobsMapper
{
    public static ListRepositoryJobs ToQuery(ListRepositoryJobsRequest request) => new(request.Key);

    public static ListRepositoryJobsResponse ToResponse(IReadOnlyList<RepositoryJob> jobs) =>
        new(
        [
            .. jobs.Select(job => new RepositoryJobItem(
                job.Instance,
                job.InstanceName,
                job.Job,
                job.Status is { } status ? Spending.Name(status) : null,
                job.Hold is { } hold ? Spending.Name(hold) : null,
                job.Attempts,
                job.LastOutcome is { } outcome ? Spending.Name(outcome) : null,
                Spending.Tokens(job.Tokens),
                Spending.Costs(job.Costs),
                job.FirstActivityAt,
                job.LastActivityAt)),
        ]);
}
