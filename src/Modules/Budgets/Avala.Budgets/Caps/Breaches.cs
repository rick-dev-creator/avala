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

    public const string MemoryUnit = "megabytes";

    private const decimal Megabyte = 1024 * 1024;

    public static BudgetCaps Unlimited { get; } = new([], Option<long>.None, Option<double>.None);

    extension(SessionBudget budget)
    {
        public Option<BudgetBreach> BreachBy(Commitment committed, IReadOnlyList<UsageLimit> limits, long memoryBytes) =>
            budget.Error.Match(
                error => new BudgetBreach(BudgetMeasure.Declaration, BudgetFile, 0, 0, error),
                () => Costs(budget.Caps, committed)
                    .Concat(Tokens(budget.Caps, committed))
                    .Concat(Limits(budget.Caps, limits))
                    .Concat(Memory(budget.Caps, memoryBytes))
                    .FirstOrDefault()
                    .ToOption());
    }

    extension(BudgetBreach breach)
    {
        public HoldReason Reason => breach.Measure switch
        {
            BudgetMeasure.Limit => HoldReason.LimitNearlyReached,
            BudgetMeasure.Declaration => HoldReason.InvalidBudget,
            BudgetMeasure.Memory => HoldReason.MemoryExceeded,
            _ => HoldReason.BudgetExceeded,
        };
    }

    private static IEnumerable<BudgetBreach> Costs(BudgetCaps caps, Commitment committed) =>
        from cap in caps.CostPerJob
        let amount = committed.In(cap.Currency)
        where amount >= cap.Amount
        select new BudgetBreach(BudgetMeasure.Cost, cap.Currency, amount, cap.Amount, Option<BudgetError>.None);

    private static IEnumerable<BudgetBreach> Tokens(BudgetCaps caps, Commitment committed) =>
        caps.TokensPerJob.Match<IEnumerable<BudgetBreach>>(
            cap => committed.Tokens >= cap ? [new BudgetBreach(BudgetMeasure.Tokens, TokenUnit, committed.Tokens, cap, Option<BudgetError>.None)] : [],
            () => []);

    private static IEnumerable<BudgetBreach> Limits(BudgetCaps caps, IReadOnlyList<UsageLimit> limits) =>
        caps.HoldAtLimit.Match(
            threshold => limits
                .Where(limit => limit.UsedFraction >= threshold)
                .Select(limit => new BudgetBreach(BudgetMeasure.Limit, limit.Window, (decimal)limit.UsedFraction, (decimal)threshold, Option<BudgetError>.None)),
            () => []);

    private static IEnumerable<BudgetBreach> Memory(BudgetCaps caps, long memoryBytes) =>
        caps.MemoryPerJobMegabytes.Match<IEnumerable<BudgetBreach>>(
            cap => memoryBytes / Megabyte >= cap
                ? [new BudgetBreach(BudgetMeasure.Memory, MemoryUnit, Math.Round(memoryBytes / Megabyte, 1), cap, Option<BudgetError>.None)]
                : [],
            () => []);
}
