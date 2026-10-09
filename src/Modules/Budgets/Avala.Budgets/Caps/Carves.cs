using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Caps;

internal sealed record Commitment(IReadOnlyList<Cost> Costs, long Tokens)
{
    public static Commitment Nothing { get; } = new([], 0);

    public static Commitment Of(UsageSummary spent) =>
        new(
            [.. spent.Costs.GroupBy(cost => cost.Currency, StringComparer.Ordinal).Select(group => new Cost(group.Sum(cost => cost.Amount), group.Key))],
            spent.Tokens.Input + spent.Tokens.Output + spent.Tokens.CacheRead + spent.Tokens.CacheWrite + spent.Tokens.Reasoning);

    public decimal In(string currency) => Costs.Where(cost => cost.Currency == currency).Sum(cost => cost.Amount);

    public Commitment Plus(Commitment other) =>
        new(Combine(Costs, other.Costs, (mine, theirs) => mine + theirs), Tokens + other.Tokens);

    public Commitment AtLeast(BudgetCarve carve) =>
        new(Combine(Costs, carve.Cost, Math.Max), carve.Tokens.Match(tokens => Math.Max(Tokens, tokens), () => Tokens));

    private static IReadOnlyList<Cost> Combine(IReadOnlyList<Cost> left, IReadOnlyList<Cost> right, Func<decimal, decimal, decimal> merge) =>
        [
            .. left.Select(cost => cost.Currency).Union(right.Select(cost => cost.Currency), StringComparer.Ordinal)
                .Select(currency => new Cost(
                    merge(left.Where(cost => cost.Currency == currency).Sum(cost => cost.Amount), right.Where(cost => cost.Currency == currency).Sum(cost => cost.Amount)),
                    currency)),
        ];
}

internal sealed record Lineage(IReadOnlyList<BudgetCarve> Carves, Func<JobId, Commitment> Spent, Func<JobId, bool> Ended)
{
    public Commitment CommittedBy(JobId job) =>
        Carves
            .Where(carve => carve.Parent == job)
            .Aggregate(Spent(job), (committed, carve) => committed.Plus(Reserved(carve)));

    private Commitment Reserved(BudgetCarve carve) =>
        Ended(carve.Child) ? CommittedBy(carve.Child) : CommittedBy(carve.Child).AtLeast(carve);
}

internal static class Carves
{
    public const double DefaultShare = 0.5;

    extension(BudgetCaps caps)
    {
        public BudgetCaps Within(Option<BudgetCarve> carve) =>
            carve.Match(
                given => caps with
                {
                    CostPerJob =
                    [
                        .. caps.CostPerJob.Select(cap => cap.Currency)
                            .Union(given.Cost.Select(cap => cap.Currency), StringComparer.Ordinal)
                            .Select(currency => new Cost(
                                Smallest(caps.CostPerJob.Concat(given.Cost).Where(cap => cap.Currency == currency).Select(cap => cap.Amount)),
                                currency)),
                    ],
                    TokensPerJob = caps.TokensPerJob.Match(
                        own => given.Tokens.Match(carved => Math.Min(own, carved), () => own),
                        () => given.Tokens),
                },
                () => caps);

        public BudgetCarve CarveFor(JobId parent, JobId child, Commitment committed, DateTimeOffset at)
        {
            var share = caps.CarvePerChild.Match(declared => declared, () => DefaultShare);

            return new BudgetCarve(
                parent,
                child,
                [.. caps.CostPerJob.Select(cap => new Cost(Math.Max(0, cap.Amount - committed.In(cap.Currency)) * (decimal)share, cap.Currency))],
                caps.TokensPerJob.Map(cap => (long)Math.Floor(Math.Max(0, cap - committed.Tokens) * share)),
                share,
                at);
        }
    }

    private static decimal Smallest(IEnumerable<decimal> amounts) => amounts.Min();
}
