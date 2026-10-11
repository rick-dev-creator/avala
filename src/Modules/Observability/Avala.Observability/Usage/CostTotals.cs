using Avala.Agents.Contracts.Events;

namespace Avala.Observability.Usage;

internal static class CostTotals
{
    extension(IEnumerable<Cost> costs)
    {
        public IReadOnlyList<Cost> Summed() =>
        [
            .. costs
                .GroupBy(cost => (cost.Currency, cost.Basis))
                .OrderBy(group => group.Key.Currency, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Basis)
                .Select(group => new Cost(group.Sum(cost => cost.Amount), group.Key.Currency) { Basis = group.Key.Basis }),
        ];
    }
}
