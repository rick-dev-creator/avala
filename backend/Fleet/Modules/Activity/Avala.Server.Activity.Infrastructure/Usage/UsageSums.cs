using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Usage;
using Avala.Server.Activity.Domain;
using Avala.Server.Activity.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Server.Activity.Infrastructure.Usage;

internal sealed record QuarterTokens(DateTimeOffset Quarter, TokenSums Tokens);

internal sealed record QuarterCost(DateTimeOffset Quarter, CostSum Cost);

internal sealed record ShareTokens(UsageShareKind Kind, string Key, TokenSums Tokens);

internal sealed record ShareCost(UsageShareKind Kind, string Key, CostSum Cost);

internal sealed record JobPair(Guid Instance, Guid Job, string? Repository);

internal sealed record UsageRows(
    IReadOnlyList<QuarterTokens> QuarterTokens,
    IReadOnlyList<QuarterCost> QuarterCosts,
    IReadOnlyList<ShareTokens> ShareTokens,
    IReadOnlyList<ShareCost> ShareCosts,
    IReadOnlyList<JobPair> Jobs,
    IReadOnlyDictionary<Guid, string> Names);

internal sealed class UsageSums(ActivityDbContext database, UsageRequest request)
{
    public async Task<UsageRows> ReadAsync(CancellationToken cancellationToken)
    {
        List<ShareTokens> shareTokens = [];
        List<ShareCost> shareCosts = [];
        var quarterTokens = await QuarterTokensAsync(cancellationToken);
        var quarterCosts = await QuarterCostsAsync(cancellationToken);
        shareTokens.AddRange(await RepositoryTokensAsync(cancellationToken));
        shareCosts.AddRange(await RepositoryCostsAsync(cancellationToken));
        shareTokens.AddRange(await InstanceTokensAsync(cancellationToken));
        shareCosts.AddRange(await InstanceCostsAsync(cancellationToken));
        shareTokens.AddRange(await ModelTokensAsync(cancellationToken));
        shareCosts.AddRange(await ModelCostsAsync(cancellationToken));
        var jobs = await JobsAsync(cancellationToken);
        var instances = shareTokens.Where(share => share.Kind == UsageShareKind.Instance).Select(share => Guid.Parse(share.Key)).ToList();
        var names = await database.Instances.AsNoTracking()
            .Where(instance => instances.Contains(instance.Id))
            .ToDictionaryAsync(instance => instance.Id, instance => instance.Name, cancellationToken);

        return new UsageRows(quarterTokens, quarterCosts, shareTokens, shareCosts, jobs, names);
    }

    private IQueryable<QuarterUsage> Buckets()
    {
        var start = request.Periods[0].StartsAt;
        var end = request.Periods[^1].EndsAt;
        var buckets = database.Quarters.AsNoTracking().Where(bucket => bucket.Quarter >= start && bucket.Quarter < end);

        return request.Repository is { } repository
            ? buckets.Where(bucket => database.Jobs.Any(job => job.InstanceId == bucket.InstanceId && job.JobId == bucket.JobId && job.Repository == repository))
            : buckets;
    }

    private IQueryable<OwnedBucket> Owned() =>
        from bucket in Buckets()
        join job in database.Jobs.AsNoTracking() on new { bucket.InstanceId, bucket.JobId } equals new { job.InstanceId, job.JobId } into owners
        from job in owners.DefaultIfEmpty()
        select new OwnedBucket { Bucket = bucket, Outside = bucket.JobId == Guid.Empty, Repository = job == null ? null : job.Repository };

    private async Task<List<QuarterTokens>> QuarterTokensAsync(CancellationToken cancellationToken) =>
        [
            .. (await Buckets()
                .GroupBy(bucket => bucket.Quarter)
                .Select(group => new
                {
                    group.Key,
                    Input = group.Sum(bucket => bucket.Tokens.Input),
                    Output = group.Sum(bucket => bucket.Tokens.Output),
                    CacheRead = group.Sum(bucket => bucket.Tokens.CacheRead),
                    CacheWrite = group.Sum(bucket => bucket.Tokens.CacheWrite),
                    Reasoning = group.Sum(bucket => bucket.Tokens.Reasoning),
                })
                .ToListAsync(cancellationToken))
                .Select(row => new QuarterTokens(row.Key, new TokenSums(row.Input, row.Output, row.CacheRead, row.CacheWrite, row.Reasoning))),
        ];

    private async Task<List<QuarterCost>> QuarterCostsAsync(CancellationToken cancellationToken) =>
        [
            .. (await Buckets()
                .SelectMany(bucket => bucket.Costs, (bucket, cost) => new { bucket.Quarter, cost.Currency, cost.Basis, cost.Amount })
                .GroupBy(cost => new { cost.Quarter, cost.Currency, cost.Basis })
                .Select(group => new { group.Key.Quarter, group.Key.Currency, group.Key.Basis, Amount = group.Sum(cost => cost.Amount) })
                .ToListAsync(cancellationToken))
                .Select(row => new QuarterCost(row.Quarter, new CostSum(row.Currency, row.Basis.ToContract(), row.Amount))),
        ];

    private async Task<List<ShareTokens>> RepositoryTokensAsync(CancellationToken cancellationToken) =>
        [
            .. (await Owned()
                .GroupBy(owned => new { owned.Outside, owned.Repository })
                .Select(group => new
                {
                    group.Key.Outside,
                    group.Key.Repository,
                    Input = group.Sum(owned => owned.Bucket.Tokens.Input),
                    Output = group.Sum(owned => owned.Bucket.Tokens.Output),
                    CacheRead = group.Sum(owned => owned.Bucket.Tokens.CacheRead),
                    CacheWrite = group.Sum(owned => owned.Bucket.Tokens.CacheWrite),
                    Reasoning = group.Sum(owned => owned.Bucket.Tokens.Reasoning),
                })
                .ToListAsync(cancellationToken))
                .Select(row => new ShareTokens(
                    RepositoryKind(row.Outside, row.Repository),
                    row.Repository?.Value ?? string.Empty,
                    new TokenSums(row.Input, row.Output, row.CacheRead, row.CacheWrite, row.Reasoning))),
        ];

    private async Task<List<ShareCost>> RepositoryCostsAsync(CancellationToken cancellationToken) =>
        [
            .. (await Owned()
                .SelectMany(owned => owned.Bucket.Costs, (owned, cost) => new { owned.Outside, owned.Repository, cost.Currency, cost.Basis, cost.Amount })
                .GroupBy(cost => new { cost.Outside, cost.Repository, cost.Currency, cost.Basis })
                .Select(group => new { group.Key.Outside, group.Key.Repository, group.Key.Currency, group.Key.Basis, Amount = group.Sum(cost => cost.Amount) })
                .ToListAsync(cancellationToken))
                .Select(row => new ShareCost(
                    RepositoryKind(row.Outside, row.Repository),
                    row.Repository?.Value ?? string.Empty,
                    new CostSum(row.Currency, row.Basis.ToContract(), row.Amount))),
        ];

    private async Task<List<ShareTokens>> InstanceTokensAsync(CancellationToken cancellationToken) =>
        [
            .. (await Buckets()
                .GroupBy(bucket => bucket.InstanceId)
                .Select(group => new
                {
                    group.Key,
                    Input = group.Sum(bucket => bucket.Tokens.Input),
                    Output = group.Sum(bucket => bucket.Tokens.Output),
                    CacheRead = group.Sum(bucket => bucket.Tokens.CacheRead),
                    CacheWrite = group.Sum(bucket => bucket.Tokens.CacheWrite),
                    Reasoning = group.Sum(bucket => bucket.Tokens.Reasoning),
                })
                .ToListAsync(cancellationToken))
                .Select(row => new ShareTokens(UsageShareKind.Instance, row.Key.ToString(), new TokenSums(row.Input, row.Output, row.CacheRead, row.CacheWrite, row.Reasoning))),
        ];

    private async Task<List<ShareCost>> InstanceCostsAsync(CancellationToken cancellationToken) =>
        [
            .. (await Buckets()
                .SelectMany(bucket => bucket.Costs, (bucket, cost) => new { bucket.InstanceId, cost.Currency, cost.Basis, cost.Amount })
                .GroupBy(cost => new { cost.InstanceId, cost.Currency, cost.Basis })
                .Select(group => new { group.Key.InstanceId, group.Key.Currency, group.Key.Basis, Amount = group.Sum(cost => cost.Amount) })
                .ToListAsync(cancellationToken))
                .Select(row => new ShareCost(UsageShareKind.Instance, row.InstanceId.ToString(), new CostSum(row.Currency, row.Basis.ToContract(), row.Amount))),
        ];

    private async Task<List<ShareTokens>> ModelTokensAsync(CancellationToken cancellationToken) =>
        [
            .. (await Buckets()
                .GroupBy(bucket => bucket.Model)
                .Select(group => new
                {
                    group.Key,
                    Input = group.Sum(bucket => bucket.Tokens.Input),
                    Output = group.Sum(bucket => bucket.Tokens.Output),
                    CacheRead = group.Sum(bucket => bucket.Tokens.CacheRead),
                    CacheWrite = group.Sum(bucket => bucket.Tokens.CacheWrite),
                    Reasoning = group.Sum(bucket => bucket.Tokens.Reasoning),
                })
                .ToListAsync(cancellationToken))
                .Select(row => new ShareTokens(UsageShareKind.Model, row.Key, new TokenSums(row.Input, row.Output, row.CacheRead, row.CacheWrite, row.Reasoning))),
        ];

    private async Task<List<ShareCost>> ModelCostsAsync(CancellationToken cancellationToken) =>
        [
            .. (await Buckets()
                .SelectMany(bucket => bucket.Costs, (bucket, cost) => new { bucket.Model, cost.Currency, cost.Basis, cost.Amount })
                .GroupBy(cost => new { cost.Model, cost.Currency, cost.Basis })
                .Select(group => new { group.Key.Model, group.Key.Currency, group.Key.Basis, Amount = group.Sum(cost => cost.Amount) })
                .ToListAsync(cancellationToken))
                .Select(row => new ShareCost(UsageShareKind.Model, row.Model, new CostSum(row.Currency, row.Basis.ToContract(), row.Amount))),
        ];

    private async Task<List<JobPair>> JobsAsync(CancellationToken cancellationToken) =>
        [
            .. (await Owned()
                .Where(owned => !owned.Outside)
                .Select(owned => new { owned.Bucket.InstanceId, owned.Bucket.JobId, owned.Repository })
                .Distinct()
                .ToListAsync(cancellationToken))
                .Select(row => new JobPair(row.InstanceId, row.JobId, row.Repository?.Value)),
        ];

    private static UsageShareKind RepositoryKind(bool outside, RepositoryKey? repository) =>
        outside ? UsageShareKind.OutsideAnyJob : repository is null ? UsageShareKind.Unknown : UsageShareKind.Repository;

    private sealed class OwnedBucket
    {
        public required QuarterUsage Bucket { get; init; }

        public required bool Outside { get; init; }

        public required RepositoryKey? Repository { get; init; }
    }
}
