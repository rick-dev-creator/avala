using Avala.Fleet.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.FleetActivity;

internal sealed record FleetStack(double Input, double Output, double Reasoning, double CacheWrite)
{
    public static FleetStack Flat { get; } = new(0, 0, 0, 0);
}

internal sealed record FleetColumn(double Share, Option<double> Previous, FleetStack Stack, string Tick, string Tip);

internal static class FleetColumns
{
    public const int MostTicks = 12;

    public static IReadOnlyList<FleetColumn> Of(
        IReadOnlyList<FleetPeriod> series,
        FleetStep step,
        bool spend,
        Option<FleetCostKind> reference,
        IReadOnlyList<FleetPeriod> earlier)
    {
        var values = series.Select(period => Value(period, spend, reference)).ToList();
        var highest = Highest([.. series, .. earlier], spend, reference);
        var every = Math.Max(1, (int)Math.Ceiling(series.Count / (double)MostTicks));
        double Scaled(decimal value) => highest == 0 ? 0 : (double)(value / highest);

        return
        [
            .. series.Select((period, index) => new FleetColumn(
                Scaled(values[index]),
                index < earlier.Count ? Scaled(Value(earlier[index], spend, reference)) : Option<double>.None,
                spend ? FleetStack.Flat : Stacked(period.Tokens, Scaled),
                index % every == 0 ? FleetFigures.Tick(period.Start, step, compact: series.Count > 14) : string.Empty,
                Tip(period, index < earlier.Count ? earlier[index] : null, step))),
        ];
    }

    public static IReadOnlyList<double> Shares(IReadOnlyList<FleetPeriod> series, bool spend, Option<FleetCostKind> reference)
    {
        var highest = Highest(series, spend, reference);

        return [.. series.Select(period => highest == 0 ? 0 : (double)(Value(period, spend, reference) / highest))];
    }

    public static decimal Highest(IReadOnlyList<FleetPeriod> series, bool spend, Option<FleetCostKind> reference) =>
        series.Select(period => Value(period, spend, reference)).DefaultIfEmpty(0).Max();

    public static decimal Value(FleetPeriod period, bool spend, Option<FleetCostKind> reference) =>
        spend ? FleetFigures.Spent(period.Costs, reference) : FleetFigures.Total(period.Tokens);

    private static FleetStack Stacked(FleetTokens tokens, Func<decimal, double> scaled) =>
        new(
            scaled(tokens.Input),
            scaled(tokens.Input + tokens.Output),
            scaled(tokens.Input + tokens.Output + tokens.Reasoning),
            scaled(tokens.Input + tokens.Output + tokens.Reasoning + tokens.CacheWrite));

    private static string Tip(FleetPeriod period, FleetPeriod? earlier, FleetStep step) =>
        string.Join(
            " · ",
            [
                FleetFigures.Period(period.Start, step),
                FleetFigures.Spending(period.Tokens, period.Costs),
                .. earlier is null ? Array.Empty<string>() : [$"{FleetFigures.Period(earlier.Start, step)} had {FleetFigures.Spending(earlier.Tokens, earlier.Costs)}"],
            ]);
}
