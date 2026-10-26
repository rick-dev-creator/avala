using System.Globalization;
using Avala.Fleet.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.FleetActivity;

internal static class FleetFigures
{
    public static decimal Total(FleetTokens tokens) => tokens.Input + tokens.Output + tokens.CacheRead + tokens.CacheWrite + tokens.Reasoning;

    public static decimal Spent(IReadOnlyList<FleetCost> costs, Option<FleetCostKind> reference) =>
        reference.Match(kind => costs.Where(cost => cost.Currency == kind.Currency && cost.Basis == kind.Basis).Sum(cost => cost.Amount), () => 0m);

    public static string Tokens(decimal count) => count switch
    {
        >= 1_000_000_000 => (count / 1_000_000_000).ToString("0.#", CultureInfo.CurrentCulture) + "B",
        >= 1_000_000 => (count / 1_000_000).ToString("0.#", CultureInfo.CurrentCulture) + "M",
        >= 1_000 => (count / 1_000).ToString("0.#", CultureInfo.CurrentCulture) + "K",
        _ => count.ToString("0", CultureInfo.CurrentCulture),
    };

    public static string Money(decimal amount, string currency) =>
        string.Create(CultureInfo.CurrentCulture, $"{amount.ToString(amount >= 1 || amount == 0 ? "N2" : "0.####", CultureInfo.CurrentCulture)} {currency}");

    public static string Basis(string basis) => basis == "billed" ? "billed" : "at API prices";

    public static string Counted(long count, string noun) => Counted(count, noun, noun + "s");

    public static string Counted(long count, string noun, string nouns) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? noun : nouns)}");

    public static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    public static string Period(DateOnly start, FleetStep step) => step switch
    {
        FleetStep.Week => "Week of " + start.ToString("d MMM", CultureInfo.CurrentCulture),
        FleetStep.Month => start.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
        _ => start.ToString("ddd d MMM", CultureInfo.CurrentCulture),
    };

    public static string Tick(DateOnly start, FleetStep step, bool compact) => step switch
    {
        FleetStep.Month => start.ToString("MMM", CultureInfo.CurrentCulture),
        _ when compact => start.ToString("d/M", CultureInfo.CurrentCulture),
        _ => start.ToString("d MMM", CultureInfo.CurrentCulture),
    };

    public static string Repository(string key) =>
        key.StartsWith("remote:", StringComparison.Ordinal) ? key["remote:".Length..] : key;

    public static string Said(FleetReadState state) => state switch
    {
        FleetReadState.NotConnected => "This instance is not connected to a fleet server. Connect it in Settings › Fleet.",
        FleetReadState.Paused => "The fleet is paused in Settings › Fleet.",
        FleetReadState.KeyRefused => "The fleet server refused this instance's key. Check it in Settings › Fleet.",
        FleetReadState.Unreachable => "The fleet server did not answer.",
        FleetReadState.Outdated => "The fleet server has no dashboard: it is older than this Avala, or the address is not a fleet server's. Update the server or check the address in Settings › Fleet.",
        FleetReadState.Untrusted => "The fleet server's certificate is not trusted by this computer. For a server on your network, trust its root certificate: see the fleet server guide.",
        FleetReadState.Refused => "The fleet server could not answer for this range or time zone.",
        _ => string.Empty,
    };

    public static Option<decimal> CacheHit(FleetTokens tokens)
    {
        var input = tokens.Input + tokens.CacheRead + tokens.CacheWrite;

        return input == 0 ? Option<decimal>.None : tokens.CacheRead / input * 100;
    }

    public static string Percent(decimal part, decimal whole) => whole == 0 || part == 0
        ? string.Empty
        : part / whole * 100 < 1 ? "<1%" : string.Create(CultureInfo.CurrentCulture, $"{part / whole * 100:0}%");

    public static string Change(decimal now, Option<decimal> before, string previous) =>
        before.Match(
            earlier => earlier == 0
                ? now == 0 ? string.Empty : $"nothing in {previous}"
                : $"{Moved(Growth(now, earlier), "%")} vs {previous}",
            () => string.Empty);

    public static string PointChange(Option<decimal> now, Option<decimal> before, string previous) =>
        now.Bind(current => before.Map(earlier => $"{Moved(Math.Round(current - earlier), " pts")} vs {previous}")).Match(change => change, () => string.Empty);

    public static string Shift(decimal now, Option<decimal> before) =>
        before.Match(earlier => earlier == 0 ? now == 0 ? string.Empty : "new" : Moved(Growth(now, earlier), "%"), () => string.Empty);

    public static string Spending(FleetTokens tokens, IReadOnlyList<FleetCost> costs) =>
        string.Join(
            " · ",
            [
                $"{Tokens(Total(tokens))} tokens",
                .. costs.Select(cost => $"{Money(cost.Amount, cost.Currency)} {Basis(cost.Basis)}"),
            ]);

    private static decimal Growth(decimal now, decimal earlier) => Math.Round((now - earlier) / earlier * 100);

    private static string Moved(decimal by, string unit) => by switch
    {
        > 0 => string.Create(CultureInfo.CurrentCulture, $"▲ {by:0}{unit}"),
        < 0 => string.Create(CultureInfo.CurrentCulture, $"▼ {-by:0}{unit}"),
        _ => "no change",
    };
}
