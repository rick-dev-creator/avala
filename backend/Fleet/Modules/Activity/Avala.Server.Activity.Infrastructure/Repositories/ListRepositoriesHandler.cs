using Ardalis.Result;
using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Repositories;
using Avala.Server.Activity.Infrastructure.Storage;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Avala.Server.Activity.Infrastructure.Repositories;

internal sealed class ListRepositoriesHandler(ActivityDbContext database) : IQueryHandler<ListRepositories, Result<FleetRepositories>>
{
    public async ValueTask<Result<FleetRepositories>> Handle(ListRepositories query, CancellationToken cancellationToken)
    {
        var attributed = database.Jobs.AsNoTracking().Where(job => job.Repository != null);
        var groups = await attributed
            .GroupBy(job => job.Repository)
            .Select(group => new
            {
                group.Key,
                Jobs = group.Count(),
                Instances = group.Select(job => job.InstanceId).Distinct().Count(),
                Input = group.Sum(job => job.Tokens.Input),
                Output = group.Sum(job => job.Tokens.Output),
                CacheRead = group.Sum(job => job.Tokens.CacheRead),
                CacheWrite = group.Sum(job => job.Tokens.CacheWrite),
                Reasoning = group.Sum(job => job.Tokens.Reasoning),
                First = group.Min(job => job.FirstActivityAt),
                Last = group.Max(job => job.LastActivityAt),
            })
            .ToListAsync(cancellationToken);
        var costs = (await attributed
            .SelectMany(job => job.Costs, (job, cost) => new { job.Repository, cost.Currency, cost.Basis, cost.Amount })
            .GroupBy(cost => new { cost.Repository, cost.Currency, cost.Basis })
            .Select(group => new { group.Key.Repository, group.Key.Currency, group.Key.Basis, Amount = group.Sum(cost => cost.Amount) })
            .ToListAsync(cancellationToken))
            .ToLookup(
                cost => cost.Repository?.Value ?? string.Empty,
                cost => new CostSum(cost.Currency, cost.Basis.ToContract(), cost.Amount),
                StringComparer.Ordinal);
        var unattributed = await database.Jobs.CountAsync(job => job.Repository == null, cancellationToken);

        return new FleetRepositories(
            [
                .. groups
                    .Select(group => (Key: group.Key?.Value ?? string.Empty, Group: group))
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new RepositoryActivity(
                        entry.Key,
                        entry.Group.Jobs,
                        entry.Group.Instances,
                        new TokenSums(entry.Group.Input, entry.Group.Output, entry.Group.CacheRead, entry.Group.CacheWrite, entry.Group.Reasoning),
                        [.. costs[entry.Key].OrderBy(cost => cost.Currency, StringComparer.Ordinal).ThenBy(cost => cost.Basis)],
                        entry.Group.First,
                        entry.Group.Last)),
            ],
            unattributed);
    }
}
