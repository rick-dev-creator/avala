using System.Text.Json;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.PolicyFiles;

internal static class PolicyFileParser
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 4, AllowDuplicateProperties = false };

    private static readonly string[] Fields = ["name", "kind", "target", "within", "answer"];

    private static readonly string[] Sections = ["rules", "autonomy", "formAnswers"];

    public static Result<PermissionPolicy, PolicyError> Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return Policy(document.RootElement);
        }
        catch (JsonException)
        {
            return PolicyError.Malformed;
        }
    }

    private static Result<PermissionPolicy, PolicyError> Policy(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return PolicyError.Malformed;
        }

        if (root.EnumerateObject().Any(property => !Sections.Contains(property.Name, StringComparer.Ordinal)))
        {
            return PolicyError.UnknownField;
        }

        if (!Text(root, "autonomy").Bind(text => Named<Autonomy>(text, PolicyError.UnknownAutonomy)).TryGetValue(out var autonomy, out var error)
            || !Text(root, "formAnswers").Bind(text => Named<FormStrategy>(text, PolicyError.UnknownStrategy)).TryGetValue(out var strategy, out error)
            || !Rules(root).TryGetValue(out var rules, out error))
        {
            return error;
        }

        return new PermissionPolicy(
            rules,
            autonomy.Match(level => level, () => Autonomy.Supervised),
            strategy.Match(chosen => chosen, () => FormStrategy.Recommended));
    }

    private static Result<IReadOnlyList<PolicyRule>, PolicyError> Rules(JsonElement root)
    {
        if (!root.TryGetProperty("rules", out var rules))
        {
            return Result<IReadOnlyList<PolicyRule>, PolicyError>.Success([]);
        }

        if (rules.ValueKind != JsonValueKind.Array)
        {
            return PolicyError.Malformed;
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

    private static Result<PolicyRule, PolicyError> Rule(JsonElement element, int position) =>
        element.ValueKind != JsonValueKind.Object ? PolicyError.Malformed
        : element.EnumerateObject().Any(property => !Fields.Contains(property.Name, StringComparer.Ordinal)) ? PolicyError.UnknownField
        : Declared(element, position);

    private static Result<PolicyRule, PolicyError> Declared(JsonElement element, int position)
    {
        if (!Text(element, "name").TryGetValue(out var name, out var error)
            || !Kind(element).TryGetValue(out var kind, out error)
            || !Text(element, "target").TryGetValue(out var target, out error)
            || !Scope(element).TryGetValue(out var scope, out error)
            || !Answer(element).TryGetValue(out var answer, out error))
        {
            return error;
        }

        var within = scope.Match(value => value, () => RuleScope.Anywhere);

        return within == RuleScope.Workspace && kind.Match(value => value != ItemKind.FileEdit, () => false)
            ? PolicyError.ScopeNeedsFileEdits
            : new PolicyRule(
                RuleOrigin.Repository,
                name.Match(value => value, () => $"rule {position}"),
                kind,
                target,
                within,
                answer.Match(value => value, () => PolicyAnswer.Ask));
    }

    private static Result<Option<ItemKind>, PolicyError> Kind(JsonElement element) =>
        Text(element, "kind").Bind(text => Named<ItemKind>(text, PolicyError.UnknownKind));

    private static Result<Option<RuleScope>, PolicyError> Scope(JsonElement element) =>
        Text(element, "within").Bind(text => Named<RuleScope>(text, PolicyError.UnknownScope)).Bind(Declarable);

    private static Result<Option<PolicyAnswer>, PolicyError> Answer(JsonElement element) =>
        Text(element, "answer").Bind(text => text.IsSome ? Named<PolicyAnswer>(text, PolicyError.UnknownAnswer) : PolicyError.MissingAnswer);

    private static Result<Option<RuleScope>, PolicyError> Declarable(Option<RuleScope> scope) =>
        scope == Option<RuleScope>.Some(RuleScope.OutsideWorkspace) ? PolicyError.UnknownScope : scope;

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
