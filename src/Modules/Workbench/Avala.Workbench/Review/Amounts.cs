using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Observability.Contracts;

namespace Avala.Workbench.Review;

internal static class Amounts
{
    public static string Costs(IEnumerable<Cost> costs) =>
        string.Join(", ", costs.Select(cost => string.Create(CultureInfo.InvariantCulture, $"{cost.Currency} {cost.Amount:0.####}")));

    public static long Total(TokenUsage tokens) => tokens.Input + tokens.Output + tokens.CacheRead + tokens.CacheWrite + tokens.Reasoning;

    public static string Tokens(long tokens) => string.Create(CultureInfo.InvariantCulture, $"{tokens:N0} tokens");

    public static IReadOnlyList<string> Spent(UsageSummary usage) =>
        [.. Priced(usage.Costs), Tokens(Total(usage.Tokens))];

    public static string[] Priced(IReadOnlyList<Cost> costs) => costs.Count > 0 ? [Costs(costs)] : [];

    public static string Joined(string[] parts) => string.Join(" · ", parts);

    public static string Count(int count, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? one : many)}");
}
