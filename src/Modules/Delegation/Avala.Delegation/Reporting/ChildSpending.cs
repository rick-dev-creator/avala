using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Observability.Contracts;

namespace Avala.Delegation.Reporting;

internal sealed class ChildSpending(IUsage usage, IUsageSessions sessions, IEnumerable<IBudgets> budgets)
{
    public ChildReport Of(ChildReport report)
    {
        var spent = usage.OfJob(report.Child);

        return report with
        {
            Spent = spent.Match<IReadOnlyList<Cost>>(summary => [.. summary.Costs], () => []),
            Tokens = spent.Match(summary => Total(summary.Tokens), () => 0L),
            Carve = budgets.Select(budget => budget.CarveOf(report.Child)).FirstOrDefault(carve => carve.IsSome),
            Harness = sessions.Sessions().LastOrDefault(session => session.Job == Avala.Sdk.Option<Avala.Jobs.Contracts.JobId>.Some(report.Child)) is { } seen ? seen.Provider.Name : Avala.Sdk.Option<string>.None,
        };
    }

    private static long Total(TokenUsage tokens) => tokens.Input + tokens.Output + tokens.CacheRead + tokens.CacheWrite + tokens.Reasoning;
}
