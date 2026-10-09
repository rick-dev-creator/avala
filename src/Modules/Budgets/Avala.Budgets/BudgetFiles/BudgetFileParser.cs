using System.Text.Json;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.BudgetFiles;

internal static class BudgetFileParser
{
    private const string CostPerJob = "costPerJob";
    private const string TokensPerJob = "tokensPerJob";
    private const string HoldAtLimit = "holdAtLimit";

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 3, AllowDuplicateProperties = false };

    private static readonly string[] Fields = [CostPerJob, TokensPerJob, HoldAtLimit];

    public static Result<BudgetCaps, BudgetError> Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return Caps(document.RootElement);
        }
        catch (JsonException)
        {
            return BudgetError.Malformed;
        }
    }

    private static Result<BudgetCaps, BudgetError> Caps(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return BudgetError.Malformed;
        }

        if (root.EnumerateObject().Any(property => !Fields.Contains(property.Name, StringComparer.Ordinal)))
        {
            return BudgetError.UnknownField;
        }

        if (!Costs(root).TryGetValue(out var costs, out var error)
            || !Tokens(root).TryGetValue(out var tokens, out error)
            || !Threshold(root).TryGetValue(out var threshold, out error))
        {
            return error;
        }

        return new BudgetCaps(costs, tokens, threshold);
    }

    private static Result<IReadOnlyList<Cost>, BudgetError> Costs(JsonElement root)
    {
        if (!root.TryGetProperty(CostPerJob, out var caps))
        {
            return Result<IReadOnlyList<Cost>, BudgetError>.Success([]);
        }

        if (caps.ValueKind != JsonValueKind.Object)
        {
            return BudgetError.Malformed;
        }

        var parsed = new List<Cost>();

        foreach (var cap in caps.EnumerateObject())
        {
            if (cap.Value.ValueKind != JsonValueKind.Number)
            {
                return BudgetError.Malformed;
            }

            if (string.IsNullOrWhiteSpace(cap.Name) || !cap.Value.TryGetDecimal(out var amount) || amount <= 0)
            {
                return BudgetError.InvalidCost;
            }

            parsed.Add(new Cost(amount, cap.Name));
        }

        return parsed;
    }

    private static Result<Option<long>, BudgetError> Tokens(JsonElement root) =>
        !root.TryGetProperty(TokensPerJob, out var cap) ? Option<long>.None
        : cap.ValueKind != JsonValueKind.Number ? BudgetError.Malformed
        : cap.TryGetInt64(out var tokens) && tokens > 0 ? Option<long>.Some(tokens)
        : BudgetError.InvalidTokens;

    private static Result<Option<double>, BudgetError> Threshold(JsonElement root) =>
        !root.TryGetProperty(HoldAtLimit, out var threshold) ? Option<double>.None
        : threshold.ValueKind != JsonValueKind.Number ? BudgetError.Malformed
        : threshold.TryGetDouble(out var fraction) && fraction > 0 && fraction <= 1 ? Option<double>.Some(fraction)
        : BudgetError.InvalidThreshold;
}
