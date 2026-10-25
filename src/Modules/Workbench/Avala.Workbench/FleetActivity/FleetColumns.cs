using Avala.Fleet.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.FleetActivity;

internal sealed record FleetColumn(double Share, string Tick, string Tip);

internal static class FleetColumns
{
    public const int MostTicks = 12;

    public static IReadOnlyList<FleetColumn> Of(IReadOnlyList<FleetPeriod> series, FleetStep step, bool spend, Option<FleetCostKind> reference)
    {
        var values = series.Select(period => Value(period, spend, reference)).ToList();
        var highest = values.DefaultIfEmpty(0).Max();
        var every = Math.Max(1, (int)Math.Ceiling(series.Count / (double)MostTicks));

        return
        [
            .. series.Select((period, index) => new FleetColumn(
                highest == 0 ? 0 : (double)(values[index] / highest),
                index % every == 0 ? FleetFigures.Tick(period.Start, step, compact: series.Count > 14) : string.Empty,
                $"{FleetFigures.Period(period.Start, step)} · {FleetFigures.Spending(period.Tokens, period.Costs)}")),
        ];
    }

    public static decimal Highest(IReadOnlyList<FleetPeriod> series, bool spend, Option<FleetCostKind> reference) =>
        series.Select(period => Value(period, spend, reference)).DefaultIfEmpty(0).Max();

    public static decimal Value(FleetPeriod period, bool spend, Option<FleetCostKind> reference) =>
        spend ? FleetFigures.Spent(period.Costs, reference) : FleetFigures.Total(period.Tokens);
}
