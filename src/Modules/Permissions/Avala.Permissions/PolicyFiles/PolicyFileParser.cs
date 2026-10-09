using System.Text.Json;
using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.PolicyFiles;

internal static class PolicyFileParser
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 4, AllowDuplicateProperties = false };

    private static readonly string[] Fields = ["name", "kind", "target", "within", "answer"];

    public static Result<IReadOnlyList<PolicyRule>, PolicyError> Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return Rules(document.RootElement);
        }
        catch (JsonException)
        {
            return PolicyError.Malformed;
        }
    }

    private static Result<IReadOnlyList<PolicyRule>, PolicyError> Rules(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
        {
            return PolicyError.Malformed;
        }

        if (root.EnumerateObject().Any(property => property.Name != "rules"))
        {
            return PolicyError.UnknownField;
        }

        var parsed = new List<PolicyRule>();

        foreach (var element in rules.EnumerateArray())
        {
            if (!Rule(element, parsed.Count + 1).TryGetValue(out var rule, out var error))
            {
                return error;
            }

            parsed.Add(rule);
        }

        return parsed;
    }

    private static Result<PolicyRule, PolicyError> Rule(JsonElement element, int position)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return PolicyError.Malformed;
        }

        if (element.EnumerateObject().Any(property => !Fields.Contains(property.Name, StringComparer.Ordinal)))
        {
            return PolicyError.UnknownField;
        }

        if (!Text(element, "name").TryGetValue(out var name, out var error)
            || !Text(element, "kind").Bind(text => Named<ItemKind>(text, PolicyError.UnknownKind)).TryGetValue(out var kind, out error)
            || !Text(element, "target").TryGetValue(out var target, out error)
            || !Text(element, "within").Bind(text => Named<RuleScope>(text, PolicyError.UnknownScope)).TryGetValue(out var scope, out error)
            || !Text(element, "answer").Bind(text => text.IsSome
                ? Named<PolicyAnswer>(text, PolicyError.UnknownAnswer)
                : PolicyError.MissingAnswer).TryGetValue(out var answer, out error))
        {
            return error;
        }

        var within = scope.Match(value => value, () => RuleScope.Anywhere);

        if (within == RuleScope.Workspace && kind.Match(value => value != ItemKind.FileEdit, () => false))
        {
            return PolicyError.ScopeNeedsFileEdits;
        }

        return new PolicyRule(
            RuleOrigin.Repository,
            name.Match(value => value, () => $"rule {position}"),
            kind,
            target,
            within,
            answer.Match(value => value, () => PolicyAnswer.Ask));
    }

    private static Result<Option<string>, PolicyError> Text(JsonElement rule, string field) =>
        !rule.TryGetProperty(field, out var value) ? Option<string>.None
        : value.ValueKind == JsonValueKind.String ? Option<string>.Some(value.GetString() ?? string.Empty)
        : PolicyError.Malformed;

    private static Result<Option<T>, PolicyError> Named<T>(Option<string> text, PolicyError unknown)
        where T : struct, Enum =>
        text.Match(
            name => Enum.GetValues<T>()
                .Where(value => string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase))
                .Select(value => Option<T>.Some(value))
                .FirstOrDefault()
                .Match(value => Result<Option<T>, PolicyError>.Success(value), () => unknown),
            () => Option<T>.None);
}
