using System.Text;
using System.Text.Json;
using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Sdk;

namespace Avala.Handoffs.RuleFiles;

internal static class LimitRulesParser
{
    public const int MaximumBytes = 16 * 1024;

    public const string Section = "limits";

    private const string OnLimitField = "onLimit";

    private const string ThresholdField = "threshold";

    private const string ConnectionsField = "connections";

    private const int MostConnections = 16;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 3, AllowDuplicateProperties = false };

    public static Result<LimitRules, HandoffError> ParseMachine(string text) =>
        Read(text, root => Rules(root));

    public static Result<Option<LimitRules>, HandoffError> ParseJobFile(string text) =>
        Read(text, root =>
            root.ValueKind != JsonValueKind.Object ? HandoffError.Malformed
            : root.TryGetProperty(Section, out var section) ? Rules(section).Map(Option<LimitRules>.Some)
            : Result<Option<LimitRules>, HandoffError>.Success(Option<LimitRules>.None));

    public static Result<LimitRules, HandoffError> Rules(JsonElement section)
    {
        if (section.ValueKind != JsonValueKind.Object)
        {
            return HandoffError.Malformed;
        }

        if (section.EnumerateObject().Any(field => field.Name is not (OnLimitField or ThresholdField or ConnectionsField)))
        {
            return HandoffError.UnknownField;
        }

        return Action(section).Bind(action => Threshold(section).Bind(threshold => Connections(section).Map(connections => new LimitRules(action, threshold, connections))));
    }

    private static Result<T, HandoffError> Read<T>(string text, Func<JsonElement, Result<T, HandoffError>> read)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes)
        {
            return HandoffError.TooLarge;
        }

        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return read(document.RootElement);
        }
        catch (JsonException)
        {
            return HandoffError.Malformed;
        }
    }

    private static Result<OnLimit, HandoffError> Action(JsonElement section) =>
        !section.TryGetProperty(OnLimitField, out var action) ? OnLimit.Hold
        : action.ValueKind != JsonValueKind.String ? HandoffError.Malformed
        : action.GetString() switch
        {
            "hold" => OnLimit.Hold,
            "handoff-same-harness" => OnLimit.HandOffSameHarness,
            "handoff-any-harness" => OnLimit.HandOffAnyHarness,
            _ => HandoffError.UnknownAction,
        };

    private static Result<double, HandoffError> Threshold(JsonElement section) =>
        !section.TryGetProperty(ThresholdField, out var threshold) ? LimitRules.DefaultThreshold
        : threshold.ValueKind != JsonValueKind.Number ? HandoffError.Malformed
        : threshold.TryGetDouble(out var value) && value > 0 && value <= 1 ? value
        : HandoffError.InvalidThreshold;

    private static Result<IReadOnlyList<ConnectionName>, HandoffError> Connections(JsonElement section)
    {
        if (!section.TryGetProperty(ConnectionsField, out var listed))
        {
            return Result<IReadOnlyList<ConnectionName>, HandoffError>.Success([]);
        }

        if (listed.ValueKind != JsonValueKind.Array)
        {
            return HandoffError.Malformed;
        }

        var names = listed.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : string.Empty).ToList();

        return names.Count is 0 or > MostConnections || names.Exists(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Count
            ? HandoffError.InvalidConnections
            : Result<IReadOnlyList<ConnectionName>, HandoffError>.Success([.. names.Select(name => new ConnectionName(name))]);
    }
}
