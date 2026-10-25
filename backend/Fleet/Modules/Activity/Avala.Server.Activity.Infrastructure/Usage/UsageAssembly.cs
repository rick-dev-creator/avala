using System.Text.Json;
using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Usage;
using Avala.Server.Activity.Domain;

namespace Avala.Server.Activity.Infrastructure.Usage;

internal static class UsageAssembly
{
    private static readonly TokenSums NoTokens = new(0, 0, 0, 0, 0);

    public static UsageReport Report(UsageRequest request, UsageRows rows)
    {
        var series = Series(request.Periods, rows);
        var costs = Summed(series.SelectMany(period => period.Costs));
        var reference = Reference(costs);
        var instances = Shares(rows, UsageShareKind.Instance, reference, rows.Jobs.CountBy(job => job.Instance.ToString()).ToDictionary(StringComparer.Ordinal), key => rows.Names.GetValueOrDefault(Guid.Parse(key), string.Empty));

        return new UsageReport(
            request.From,
            request.To,
            request.Zone,
            JsonNamingPolicy.CamelCase.ConvertName(request.Step.ToString()),
            reference,
            new UsageTotals(Added(series.Select(period => period.Tokens)), costs, rows.Jobs.Count, instances.Count),
            series,
            Shares(rows, null, reference, rows.Jobs.CountBy(job => job.Repository ?? string.Empty).ToDictionary(StringComparer.Ordinal), key => key),
            instances,
            Shares(rows, UsageShareKind.Model, reference, new Dictionary<string, int>(StringComparer.Ordinal), key => key));
    }

    private static List<UsagePeriodSum> Series(IReadOnlyList<UsagePeriod> periods, UsageRows rows)
    {
        var starts = periods.Select(period => period.StartsAt).ToArray();
        var tokens = rows.QuarterTokens.ToLookup(row => PeriodOf(starts, row.Quarter), row => row.Tokens);
        var costs = rows.QuarterCosts.ToLookup(row => PeriodOf(starts, row.Quarter), row => row.Cost);

        return [.. periods.Select((period, index) => new UsagePeriodSum(period.Start, Added(tokens[index]), Summed(costs[index])))];
    }

    private static int PeriodOf(DateTimeOffset[] starts, DateTimeOffset quarter)
    {
        var found = Array.BinarySearch(starts, quarter);

        return found >= 0 ? found : ~found - 1;
    }

    private static List<UsageShare> Shares(
        UsageRows rows,
        UsageShareKind? kind,
        CostKind? reference,
        Dictionary<string, int> jobs,
        Func<string, string> name)
    {
        bool Belongs(UsageShareKind found) =>
            kind is { } wanted ? found == wanted : found is UsageShareKind.Repository or UsageShareKind.Unknown or UsageShareKind.OutsideAnyJob;

        var costs = rows.ShareCosts.Where(row => Belongs(row.Kind)).ToLookup(row => (row.Kind, row.Key), row => row.Cost);

        return
        [
            .. rows.ShareTokens
                .Where(row => Belongs(row.Kind))
                .Select(row => new UsageShare(
                    row.Kind,
                    row.Key,
                    row.Kind is UsageShareKind.Unknown or UsageShareKind.OutsideAnyJob ? string.Empty : name(row.Key),
                    row.Tokens,
                    Summed(costs[(row.Kind, row.Key)]),
                    row.Kind == UsageShareKind.OutsideAnyJob ? 0 : jobs.GetValueOrDefault(row.Key)))
                .OrderByDescending(share => Amount(share.Costs, reference))
                .ThenByDescending(share => Total(share.Tokens))
                .ThenBy(share => share.Kind)
                .ThenBy(share => share.Key, StringComparer.Ordinal),
        ];
    }

    private static CostKind? Reference(IReadOnlyList<CostSum> costs) =>
        costs.OrderByDescending(cost => cost.Amount)
            .ThenBy(cost => cost.Currency, StringComparer.Ordinal)
            .ThenBy(cost => cost.Basis)
            .Select(cost => new CostKind(cost.Currency, cost.Basis))
            .FirstOrDefault();

    private static decimal Amount(IReadOnlyList<CostSum> costs, CostKind? reference) =>
        reference is null ? 0 : costs.Where(cost => cost.Currency == reference.Currency && cost.Basis == reference.Basis).Sum(cost => cost.Amount);

    private static decimal Total(TokenSums tokens) => tokens.Input + tokens.Output + tokens.CacheRead + tokens.CacheWrite + tokens.Reasoning;

    private static TokenSums Added(IEnumerable<TokenSums> tokens) =>
        tokens.Aggregate(NoTokens, (sum, next) => new TokenSums(
            sum.Input + next.Input,
            sum.Output + next.Output,
            sum.CacheRead + next.CacheRead,
            sum.CacheWrite + next.CacheWrite,
            sum.Reasoning + next.Reasoning));

    private static List<CostSum> Summed(IEnumerable<CostSum> costs) =>
        [
            .. costs.GroupBy(cost => (cost.Currency, cost.Basis))
                .Select(group => new CostSum(group.Key.Currency, group.Key.Basis, group.Sum(cost => cost.Amount)))
                .OrderBy(cost => cost.Currency, StringComparer.Ordinal)
                .ThenBy(cost => cost.Basis),
        ];
}
