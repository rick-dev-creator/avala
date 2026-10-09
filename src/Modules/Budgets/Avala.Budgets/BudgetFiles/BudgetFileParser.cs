using System.Text.Json;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.BudgetFiles;

internal static class BudgetFileParser
{
    private const string CostPerJob = "costPerJob";
    private const string TokensPerJob = "tokensPerJob";
    private const string HoldAtLimit = "holdAtLimit";
    private const string MemoryPerJob = "memoryPerJobMegabytes";
    private const string Connections = "connections";

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 4, AllowDuplicateProperties = false };

    private static readonly string[] Fields = [CostPerJob, TokensPerJob, HoldAtLimit, MemoryPerJob];

    private static readonly string[] Sections = [.. Fields, Connections];

    public static Result<BudgetDeclaration, BudgetError> Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return Declaration(document.RootElement);
        }
        catch (JsonException)
        {
            return BudgetError.Malformed;
        }
    }

    private static Result<BudgetDeclaration, BudgetError> Declaration(JsonElement root)
    {
        if (!Caps(root, Sections).TryGetValue(out var caps, out var error))
        {
            return error;
        }

        if (!root.TryGetProperty(Connections, out var connections))
        {
            return new BudgetDeclaration(caps);
        }

        if (connections.ValueKind != JsonValueKind.Object)
        {
            return BudgetError.Malformed;
        }

        var parsed = new Dictionary<string, BudgetCaps>(StringComparer.Ordinal);

        foreach (var connection in connections.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(connection.Name))
            {
                return BudgetError.Malformed;
            }

            if (!Caps(connection.Value, Fields).TryGetValue(out var connectionCaps, out error))
            {
                return error;
            }

            parsed[connection.Name] = connectionCaps;
        }

        return new BudgetDeclaration(caps) { Connections = parsed };
    }

    private static Result<BudgetCaps, BudgetError> Caps(JsonElement root, string[] fields)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return BudgetError.Malformed;
        }

        if (root.EnumerateObject().Any(property => !fields.Contains(property.Name, StringComparer.Ordinal)))
        {
            return BudgetError.UnknownField;
        }

        if (!Costs(root).TryGetValue(out var costs, out var error)
            || !Tokens(root).TryGetValue(out var tokens, out error)
            || !Threshold(root).TryGetValue(out var threshold, out error)
            || !Memory(root).TryGetValue(out var memory, out error))
        {
            return error;
        }

        return new BudgetCaps(costs, tokens, threshold) { MemoryPerJobMegabytes = memory };
    }

    private static Result<Option<long>, BudgetError> Memory(JsonElement root) =>
        !root.TryGetProperty(MemoryPerJob, out var cap) ? Option<long>.None
        : cap.ValueKind != JsonValueKind.Number ? BudgetError.Malformed
        : cap.TryGetInt64(out var megabytes) && megabytes > 0 ? Option<long>.Some(megabytes)
        : BudgetError.InvalidMemory;

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
