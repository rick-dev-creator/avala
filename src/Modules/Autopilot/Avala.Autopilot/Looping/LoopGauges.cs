using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Looping;

internal sealed class LoopGauges(IUsage usage, IUsageHistory history, IConnections connections)
{
    public async Task<Option<UsageLimit>> ExhaustedAsync(
        Option<ConnectionName> connection,
        double threshold,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var watched = connection.IsSome ? [connection] : await CandidatesAsync(cancellationToken);
        var exhausted = watched.Select(candidate => Exhausted(candidate, threshold, now)).ToList();

        return exhausted.Count > 0 && exhausted.All(limit => limit.IsSome)
            ? exhausted.OrderBy(limit => limit.Bind(found => found.ResetsAt).Match(resets => resets, () => DateTimeOffset.MaxValue)).First()
            : Option<UsageLimit>.None;
    }

    public IReadOnlyList<Cost> SpendOf(JobId job) => usage.OfJob(job).Match(summary => summary.Costs, () => []);

    public async Task<IReadOnlyList<Cost>> WithinAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        (await history.WithinAsync(from, to, cancellationToken)).Usage.Costs;

    private Option<UsageLimit> Exhausted(Option<ConnectionName> connection, double threshold, DateTimeOffset now) =>
        Latest(LimitsOf(connection).Where(limit => limit.UsedFraction >= threshold && Pending(limit, now)));

    public Option<UsageLimit> NextReset(Option<ConnectionName> connection, DateTimeOffset now) =>
        LimitsOf(connection)
            .Where(limit => limit.ResetsAt.Match(resets => resets > now, () => false))
            .OrderBy(limit => limit.UsedFraction)
            .Select(limit => Option<UsageLimit>.Some(limit))
            .LastOrDefault();

    private async Task<IReadOnlyList<Option<ConnectionName>>> CandidatesAsync(CancellationToken cancellationToken)
    {
        var catalog = await connections.CatalogAsync(cancellationToken);

        if (catalog.DefaultMode == DefaultMode.Fixed)
        {
            return [catalog.Default];
        }

        return [.. catalog.Connections.Select(declared => Option<ConnectionName>.Some(declared.Name))];
    }

    private IEnumerable<UsageLimit> LimitsOf(Option<ConnectionName> connection) =>
        connection.Match(
            named => usage.ByConnection().Where(used => used.Connection == named).SelectMany(used => used.Usage.Limits),
            () => []);

    private static bool Pending(UsageLimit limit, DateTimeOffset now) => limit.ResetsAt.Match(resets => resets > now, () => true);

    private static Option<UsageLimit> Latest(IEnumerable<UsageLimit> limits) =>
        limits
            .OrderBy(limit => limit.ResetsAt.Match(resets => resets, () => DateTimeOffset.MaxValue))
            .Select(limit => Option<UsageLimit>.Some(limit))
            .LastOrDefault();
}
