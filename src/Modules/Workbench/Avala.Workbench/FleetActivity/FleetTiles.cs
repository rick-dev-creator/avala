using System.Globalization;
using Avala.Fleet.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.FleetActivity;

internal sealed record FleetComparison(Option<FleetUsage> Before, string Previous)
{
    public static FleetComparison None { get; } = new(Option<FleetUsage>.None, string.Empty);

    public IReadOnlyList<FleetPeriod> Series => Before.Match(usage => usage.Series, () => []);

    public string Change(FleetUsage now, Func<FleetUsage, decimal> measure) => FleetFigures.Change(measure(now), Before.Map(measure), Previous);
}

internal interface IFleetTileViewModel
{
    string Label { get; }

    string Value { get; }

    string Caption { get; }

    string Change { get; }

    bool HasChange { get; }

    IReadOnlyList<double> Spark { get; }

    bool HasSpark { get; }
}

internal sealed record FleetTileViewModel(string Label, string Value, string Caption, string Change, IReadOnlyList<double> Spark) : IFleetTileViewModel
{
    public bool HasChange => Change.Length > 0;

    public bool HasSpark => Spark.Any(share => share > 0);

    public static IReadOnlyList<FleetTileViewModel> Of(FleetUsage usage, FleetComparison comparison)
    {
        var totals = usage.Totals;
        var repositories = usage.Repositories.Count(share => share.Kind == FleetShareKind.Repository);
        decimal Spend(FleetUsage shown) => FleetFigures.Spent(shown.Totals.Costs, usage.Reference);
        decimal Tokens(FleetUsage shown) => FleetFigures.Total(shown.Totals.Tokens);

        return
        [
            usage.Reference.Match(
                reference => new FleetTileViewModel(
                    "Spend",
                    FleetFigures.Money(Spend(usage), reference.Currency),
                    string.Join(
                        " · ",
                        [
                            FleetFigures.Basis(reference.Basis),
                            .. totals.Costs
                                .Where(cost => cost.Currency != reference.Currency || cost.Basis != reference.Basis)
                                .Select(cost => $"also {FleetFigures.Money(cost.Amount, cost.Currency)} {FleetFigures.Basis(cost.Basis)}"),
                        ]),
                    comparison.Change(usage, Spend),
                    FleetColumns.Shares(usage.Series, spend: true, usage.Reference)),
                () => new FleetTileViewModel("Spend", "—", "no cost reported", string.Empty, [])),
            new(
                "Tokens",
                FleetFigures.Tokens(Tokens(usage)),
                $"{FleetFigures.Tokens(totals.Tokens.Input)} in · {FleetFigures.Tokens(totals.Tokens.Output)} out · {FleetFigures.Tokens(totals.Tokens.CacheRead)} cache read",
                comparison.Change(usage, Tokens),
                FleetColumns.Shares(usage.Series, spend: false, usage.Reference)),
            new(
                "Jobs",
                totals.Jobs.ToString("N0", CultureInfo.CurrentCulture),
                $"in {FleetFigures.Counted(repositories, "repository", "repositories")}",
                comparison.Change(usage, shown => shown.Totals.Jobs),
                []),
            PerJob(usage, comparison, usage.Reference.IsSome ? Spend : Tokens),
            new(
                "Cache hit",
                FleetFigures.CacheHit(totals.Tokens).Match(hit => string.Create(CultureInfo.CurrentCulture, $"{hit:0}%"), () => "—"),
                "of input read from cache",
                FleetFigures.PointChange(FleetFigures.CacheHit(totals.Tokens), comparison.Before.Bind(before => FleetFigures.CacheHit(before.Totals.Tokens)), comparison.Previous),
                []),
            new(
                "Instances",
                totals.Instances.ToString("N0", CultureInfo.CurrentCulture),
                "that sent usage",
                comparison.Change(usage, shown => shown.Totals.Instances),
                []),
        ];
    }

    private static FleetTileViewModel PerJob(FleetUsage usage, FleetComparison comparison, Func<FleetUsage, decimal> measure)
    {
        decimal Each(FleetUsage shown) => shown.Totals.Jobs == 0 ? 0 : measure(shown) / shown.Totals.Jobs;

        return usage.Totals.Jobs == 0
            ? new FleetTileViewModel("Per job", "—", "no job in this range", string.Empty, [])
            : new FleetTileViewModel(
                "Per job",
                usage.Reference.Match(reference => FleetFigures.Money(Each(usage), reference.Currency), () => FleetFigures.Tokens(Each(usage))),
                usage.Reference.IsSome ? "spend divided by the jobs" : "tokens divided by the jobs",
                comparison.Change(usage, Each),
                []);
    }
}
