using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Presenting;

internal static class Amounts
{
    public static string Costs(IReadOnlyList<Cost> costs) =>
        costs.Count == 0
            ? "no cost"
            : string.Join(" + ", costs.Select(cost => string.Create(CultureInfo.InvariantCulture, $"{cost.Amount:0.####} {cost.Currency}")));

    public static string Tokens(long tokens) => string.Create(CultureInfo.InvariantCulture, $"{tokens:N0} tokens");

    public static string Percent(double fraction) => string.Create(CultureInfo.InvariantCulture, $"{fraction * 100:0}%");

    public static string Megabytes(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB");

    public static string Time(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string Seconds(TimeSpan span) => string.Create(CultureInfo.InvariantCulture, $"{span.TotalSeconds:0.###}");

    public static string Unpriced(int reports) => reports switch
    {
        0 => string.Empty,
        1 => "1 usage report had no cost",
        _ => string.Create(CultureInfo.InvariantCulture, $"{reports} usage reports had no cost"),
    };

    public static string Caps(BudgetCaps caps)
    {
        string[] parts =
        [
            .. caps.CostPerJob.Select(cost => string.Create(CultureInfo.InvariantCulture, $"{cost.Amount:0.####} {cost.Currency} per job")),
            .. caps.TokensPerJob.Match<string[]>(tokens => [$"{Tokens(tokens)} per job"], () => []),
            .. caps.HoldAtLimit.Match<string[]>(hold => [$"holds at {Percent(hold)} of a limit"], () => []),
            .. caps.MemoryPerJobMegabytes.Match<string[]>(megabytes => [string.Create(CultureInfo.InvariantCulture, $"{megabytes} MB per job")], () => []),
            .. caps.CarvePerChild.Match<string[]>(share => [$"carves {Percent(share)} per child"], () => []),
        ];
        var text = string.Join(", ", parts);

        return text.Length == 0 ? "no caps" : text;
    }

    public static string Commit(Option<Workspaces.Contracts.FileOrigin> origin) =>
        origin.Match(found => found.Commit.Length > 7 ? found.Commit[..7] : found.Commit, () => "no commit");
}

internal static class ItemLists
{
    public static void ShowOnly<T>(this ObservableCollection<T> items, IEnumerable<T> next)
    {
        items.Clear();

        foreach (var item in next)
        {
            items.Add(item);
        }
    }
}
