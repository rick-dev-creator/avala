using Avala.Agents.Contracts.Events;

namespace Avala.Workbench.Presenting;

internal sealed record BudgetShare(double Weight, bool IsKept);

internal static class Shares
{
    public static double Of(IReadOnlyList<Cost> spent, IReadOnlyList<Cost> cap) =>
        cap.Count == 0 || cap[0].Amount <= 0
            ? 0
            : (double)(spent.Where(cost => cost.Currency == cap[0].Currency).Sum(cost => cost.Amount) / cap[0].Amount);

    public static IReadOnlyList<Cost> Total(IEnumerable<Cost> costs) =>
        [.. costs.GroupBy(cost => cost.Currency, StringComparer.Ordinal).Select(group => new Cost(group.Sum(cost => cost.Amount), group.Key))];

    public static decimal In(IEnumerable<Cost> costs, string currency) =>
        costs.Where(cost => cost.Currency == currency).Sum(cost => cost.Amount);
}
