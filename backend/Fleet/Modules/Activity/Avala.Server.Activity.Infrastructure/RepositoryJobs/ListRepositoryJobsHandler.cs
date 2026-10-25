using Ardalis.Result;
using Avala.Server.Activity.Contracts.RepositoryJobs;
using Avala.Server.Activity.Domain;
using Avala.Server.Activity.Infrastructure.Storage;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Avala.Server.Activity.Infrastructure.RepositoryJobs;

internal sealed class ListRepositoryJobsHandler(ActivityDbContext database)
    : IQueryHandler<ListRepositoryJobs, Result<IReadOnlyList<RepositoryJob>>>
{
    public async ValueTask<Result<IReadOnlyList<RepositoryJob>>> Handle(ListRepositoryJobs query, CancellationToken cancellationToken)
    {
        var key = RepositoryKey.Create(query.Key);

        if (key.Refusal is { } refusal)
        {
            return Result<IReadOnlyList<RepositoryJob>>.Invalid(new ValidationError(nameof(query.Key), refusal));
        }

        var repository = key.Value;
        var jobs = await database.Jobs.AsNoTracking()
            .Where(job => job.Repository == repository)
            .ToListAsync(cancellationToken);
        var instanceIds = jobs.Select(job => job.InstanceId).Distinct().ToList();
        var names = await database.Instances.AsNoTracking()
            .Where(instance => instanceIds.Contains(instance.Id))
            .Select(instance => new { instance.Id, instance.Name })
            .ToDictionaryAsync(instance => instance.Id, instance => instance.Name, cancellationToken);

        return Result<IReadOnlyList<RepositoryJob>>.Success(
        [
            .. jobs.OrderByDescending(job => job.LastActivityAt)
                .Select(job => job.ToContract(names.GetValueOrDefault(job.InstanceId, string.Empty))),
        ]);
    }
}
