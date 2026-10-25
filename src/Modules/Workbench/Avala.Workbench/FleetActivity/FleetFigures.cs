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

    public static string Spending(FleetTokens tokens, IReadOnlyList<FleetCost> costs) =>
        string.Join(
            " · ",
            [
                $"{Tokens(Total(tokens))} tokens",
                .. costs.Select(cost => $"{Money(cost.Amount, cost.Currency)} {Basis(cost.Basis)}"),
            ]);
}
