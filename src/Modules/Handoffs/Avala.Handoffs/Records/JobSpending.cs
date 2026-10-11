using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;

namespace Avala.Handoffs.Records;

internal sealed class JobSpending(IUsage usage, IUsageSessions sessions)
{
    public (IReadOnlyList<Cost> Costs, long Tokens) On(JobId job, ConnectionName connection)
    {
        var spent = sessions.Sessions()
            .Where(session => session.Job == Option<JobId>.Some(job) && session.Connection == connection)
            .SelectMany(session => usage.OfSession(session.Session).Match<UsageSummary[]>(summary => [summary], () => []))
            .ToList();

        return (
            [.. spent.SelectMany(summary => summary.Costs).GroupBy(cost => (cost.Currency, cost.Basis)).Select(group => new Cost(group.Sum(cost => cost.Amount), group.Key.Currency) { Basis = group.Key.Basis })],
            spent.Sum(summary => summary.Tokens.Input + summary.Tokens.Output + summary.Tokens.CacheRead + summary.Tokens.CacheWrite + summary.Tokens.Reasoning));
    }
}
