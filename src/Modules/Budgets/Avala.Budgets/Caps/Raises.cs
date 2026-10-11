using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Caps;

internal static class Raises
{
    extension(SpendCaps by)
    {
        public decimal In(string currency) => by.Cost.Where(cost => cost.Currency == currency).Sum(cost => cost.Amount);

        public long TokensOrNone => by.Tokens.Match(tokens => tokens, () => 0L);
    }

    extension(BudgetCaps caps)
    {
        public BudgetCaps Plus(IEnumerable<BudgetRaise> raises) => raises.Aggregate(caps, (raised, raise) => raised.Plus(raise.By));

        public BudgetCaps Plus(SpendCaps by) => caps with
        {
            CostPerJob = [.. caps.CostPerJob.Select(cap => new Cost(cap.Amount + by.In(cap.Currency), cap.Currency))],
            TokensPerJob = caps.TokensPerJob.Map(tokens => tokens + by.TokensOrNone),
        };

        public Option<SpendCaps> RaiseFrom(BudgetCaps allowance, Commitment committed) =>
            caps.CostPerJob.Count == 0 && caps.TokensPerJob.IsNone
                ? Option<SpendCaps>.None
                : new SpendCaps(
                    [
                        .. caps.CostPerJob.Select(cap => new Cost(
                            Math.Max(cap.Amount, committed.In(cap.Currency)) + allowance.CostPerJob.Where(given => given.Currency == cap.Currency).Sum(given => given.Amount) - cap.Amount,
                            cap.Currency)),
                    ],
                    caps.TokensPerJob.Map(cap => Math.Max(cap, committed.Tokens) + allowance.TokensPerJob.Match(given => given, () => 0L) - cap));
    }

    extension(BudgetCarve carve)
    {
        public BudgetCarve Plus(SpendCaps by, DateTimeOffset at) => carve with
        {
            Cost = [.. carve.Cost.Select(cost => new Cost(cost.Amount + by.In(cost.Currency), cost.Currency))],
            Tokens = carve.Tokens.Map(tokens => tokens + by.TokensOrNone),
            At = at,
        };

        public SpendCaps GrowthTo(BudgetCarve raised, Commitment committed) =>
            new(
                [
                    .. raised.Cost.Select(cost => new Cost(
                        cost.Amount - Math.Max(carve.Cost.Where(before => before.Currency == cost.Currency).Sum(before => before.Amount), committed.In(cost.Currency)),
                        cost.Currency)),
                ],
                raised.Tokens.Map(tokens => tokens - Math.Max(carve.Tokens.Match(before => before, () => 0L), committed.Tokens)));
    }
}
