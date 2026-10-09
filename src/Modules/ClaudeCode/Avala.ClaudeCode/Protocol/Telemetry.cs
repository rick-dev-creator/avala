using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Events;
using Avala.Sdk;

namespace Avala.ClaudeCode.Protocol;

internal static class Telemetry
{
    public const string PlanTool = "TodoWrite";

    private const long LatestSecond = 253_402_300_799;

    private static readonly Dictionary<string, string> Windows = new(StringComparer.Ordinal)
    {
        ["five_hour"] = "5h",
        ["seven_day"] = "7d",
        ["seven_day_opus"] = "7d opus",
        ["seven_day_sonnet"] = "7d sonnet",
    };

    public const string Currency = "USD";

    public static ValueSet<string> SubscriptionWindows { get; } = new(Windows.Values);

    public static TokenUsage Tokens(JsonNode result)
    {
        var usage = result.Members("usage");

        return new TokenUsage(
            usage.Number("input_tokens"),
            usage.Number("output_tokens"),
            usage.Number("cache_read_input_tokens"),
            usage.Number("cache_creation_input_tokens"),
            usage.Members("output_tokens_details").Number("thinking_tokens"));
    }

    public static Option<decimal> TotalCost(JsonNode result) => result.Amount("total_cost_usd");

    public static IReadOnlyList<UsageLimit> Limits(JsonNode rateLimitEvent)
    {
        var info = rateLimitEvent.Members("rate_limit_info");
        var windows = info.Members("unifiedWindows");

        return windows.Count > 0
            ? [.. windows.Where(window => window.Value is JsonObject).Select(window => Limit(window.Key, window.Value!, "utilization", "resetsAt"))]
            : info.Text("rateLimitType").Match<IReadOnlyList<UsageLimit>>(type => [Limit(type, info, "utilization", "resetsAt")], () => []);
    }

    public static IReadOnlyList<PlanStep> Plan(JsonObject input) =>
        [.. input.Items("todos").Select(todo => new PlanStep(
            todo.TextOr("content", todo.TextOr("activeForm", string.Empty)),
            todo.TextOr("status", string.Empty) switch
            {
                "completed" => PlanStepStatus.Done,
                "in_progress" => PlanStepStatus.InProgress,
                _ => PlanStepStatus.Pending,
            }))];

    private static UsageLimit Limit(string type, JsonNode window, string utilization, string resetsAt)
    {
        var resets = window.Number(resetsAt);

        return new UsageLimit(
            Windows.GetValueOrDefault(type, type),
            Math.Clamp(window.Fraction(utilization).Match(used => used, () => 0d), 0d, 1d),
            resets is > 0 and < LatestSecond ? DateTimeOffset.FromUnixTimeSeconds(resets) : Option<DateTimeOffset>.None);
    }
}
