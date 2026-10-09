using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Caps;

internal static class Breaches
{
    public const string BudgetFile = ".avala/budget.json";

    public const string TokenUnit = "tokens";

    public static BudgetCaps Unlimited { get; } = new([], Option<long>.None, Option<double>.None);

    public static UsageSummary NothingSpent { get; } = new(default, [], 0, default, []);

    extension(SessionBudget budget)
    {
        public Option<BudgetBreach> BreachBy(UsageSummary spent, IReadOnlyList<UsageLimit> limits) =>
            budget.Error.Match(
                error => new BudgetBreach(BudgetMeasure.Declaration, BudgetFile, 0, 0, error),
                () => Costs(budget.Caps, spent)
                    .Concat(Tokens(budget.Caps, spent))
                    .Concat(Limits(budget.Caps, limits))
                    .FirstOrDefault()
                    .ToOption());
    }

    extension(BudgetBreach breach)
    {
        public HoldReason Reason => breach.Measure switch
        {
            BudgetMeasure.Limit => HoldReason.LimitNearlyReached,
            BudgetMeasure.Declaration => HoldReason.InvalidBudget,
            _ => HoldReason.BudgetExceeded,
        };
    }

    private static IEnumerable<BudgetBreach> Costs(BudgetCaps caps, UsageSummary spent) =>
        from cap in caps.CostPerJob
        let amount = spent.Costs.Where(cost => cost.Currency == cap.Currency).Sum(cost => cost.Amount)
        where amount >= cap.Amount
        select new BudgetBreach(BudgetMeasure.Cost, cap.Currency, amount, cap.Amount, Option<BudgetError>.None);

    private static IEnumerable<BudgetBreach> Tokens(BudgetCaps caps, UsageSummary spent)
    {
        var tokens = spent.Tokens;
        var total = tokens.Input + tokens.Output + tokens.CacheRead + tokens.CacheWrite + tokens.Reasoning;

        return caps.TokensPerJob.Match<IEnumerable<BudgetBreach>>(
            cap => total >= cap ? [new BudgetBreach(BudgetMeasure.Tokens, TokenUnit, total, cap, Option<BudgetError>.None)] : [],
            () => []);
    }

    private static IEnumerable<BudgetBreach> Limits(BudgetCaps caps, IReadOnlyList<UsageLimit> limits) =>
        caps.HoldAtLimit.Match(
            threshold => limits
                .Where(limit => limit.UsedFraction >= threshold)
                .Select(limit => new BudgetBreach(BudgetMeasure.Limit, limit.Window, (decimal)limit.UsedFraction, (decimal)threshold, Option<BudgetError>.None)),
            () => []);
}
