using System.Text.Json;
using Avala.Agents.Contracts.Connections;
using Avala.Delegation.Contracts;
using Avala.Delegation.Policy;
using Avala.Sdk;

namespace Avala.Delegation.RepositoryFiles;

internal static class DelegationRulesParser
{
    private const string Section = "delegation";
    private const string ConnectionsField = "connections";
    private const string RoutingField = "routing";
    private const string DepthField = "maxDepth";
    private const string ChildrenField = "maxChildren";
    private const string EscalationField = "escalation";
    private const string WindowField = "parentWindowSeconds";
    private const string RoleField = "role";

    private static readonly string[] Fields = [ConnectionsField, RoutingField, DepthField, ChildrenField, EscalationField, WindowField, RoleField];

    private static readonly string[] MachineFields = [EscalationField, WindowField];

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 3, AllowDuplicateProperties = false };

    public static Result<Option<DelegationRules>, DelegationError> Parse(string jobFile)
    {
        try
        {
            using var document = JsonDocument.Parse(jobFile, Options);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return DelegationError.Malformed;
            }

            return root.TryGetProperty(Section, out var section)
                ? Declared(section).Map(Option<DelegationRules>.Some)
                : Option<DelegationRules>.None;
        }
        catch (JsonException)
        {
            return DelegationError.Malformed;
        }
    }

    private static Result<DelegationRules, DelegationError> Declared(JsonElement section)
    {
        if (section.ValueKind != JsonValueKind.Object)
        {
            return DelegationError.Malformed;
        }

        if (section.EnumerateObject().Any(field => !Fields.Contains(field.Name, StringComparer.Ordinal)))
        {
            return DelegationError.UnknownField;
        }

        return ConnectionsIn(section)
            .Bind(connections => RoutingIn(section)
                .Bind(routing => Whole(section, DepthField, DelegationRules.DefaultDepth, DelegationRules.DeepestDepth, DelegationError.InvalidDepth)
                    .Bind(depth => Whole(section, ChildrenField, DelegationRules.DefaultChildren, DelegationRules.MostChildren, DelegationError.InvalidChildren)
                        .Bind(children => EscalationIn(section)
                            .Bind(escalation => RoleIn(section)
                                .Map(role => new DelegationRules(connections, routing, depth, children) { Escalation = escalation, Role = role }))))));
    }

    public static Result<EscalationTerms, DelegationError> ParseMachine(string machineFile)
    {
        try
        {
            using var document = JsonDocument.Parse(machineFile, Options);
            var root = document.RootElement;

            return root.ValueKind != JsonValueKind.Object ? DelegationError.Malformed
                : root.EnumerateObject().Any(field => !MachineFields.Contains(field.Name, StringComparer.Ordinal)) ? DelegationError.UnknownField
                : EscalationIn(root);
        }
        catch (JsonException)
        {
            return DelegationError.Malformed;
        }
    }

    private static Result<EscalationTerms, DelegationError> EscalationIn(JsonElement section) =>
        AsksParentIn(section).Bind(asks => WindowIn(section).Map(window => new EscalationTerms(asks, window)));

    private static Result<Option<bool>, DelegationError> AsksParentIn(JsonElement section) =>
        !section.TryGetProperty(EscalationField, out var escalation) ? Option<bool>.None
        : escalation.ValueKind != JsonValueKind.String ? DelegationError.Malformed
        : escalation.GetString() switch
        {
            "human" => Option<bool>.Some(false),
            "parent" => Option<bool>.Some(true),
            _ => DelegationError.UnknownEscalation,
        };

    private static Result<Option<TimeSpan>, DelegationError> WindowIn(JsonElement section) =>
        !section.TryGetProperty(WindowField, out var window) ? Option<TimeSpan>.None
        : window.ValueKind != JsonValueKind.Number ? DelegationError.Malformed
        : window.TryGetInt32(out var seconds)
            && seconds >= EscalationTerms.ShortestWindow.TotalSeconds
            && seconds <= EscalationTerms.LongestWindow.TotalSeconds
            ? Option<TimeSpan>.Some(TimeSpan.FromSeconds(seconds))
            : DelegationError.InvalidWindow;

    private static Result<Option<ChildRole>, DelegationError> RoleIn(JsonElement section) =>
        !section.TryGetProperty(RoleField, out var role) ? Option<ChildRole>.None
        : role.ValueKind != JsonValueKind.String ? DelegationError.Malformed
        : Roles.Named(role.GetString() ?? string.Empty).Match(
            named => Result<Option<ChildRole>, DelegationError>.Success(named),
            () => DelegationError.UnknownRole);

    private static Result<IReadOnlyList<ConnectionName>, DelegationError> ConnectionsIn(JsonElement section)
    {
        if (!section.TryGetProperty(ConnectionsField, out var listed))
        {
            return Result<IReadOnlyList<ConnectionName>, DelegationError>.Success([]);
        }

        if (listed.ValueKind != JsonValueKind.Array || listed.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        {
            return DelegationError.Malformed;
        }

        var names = listed.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList();

        return names.Count is 0 or > DelegationRules.MostConnections
            || names.Any(string.IsNullOrWhiteSpace)
            || names.Distinct(StringComparer.Ordinal).Count() != names.Count
            ? DelegationError.InvalidConnections
            : Result<IReadOnlyList<ConnectionName>, DelegationError>.Success([.. names.Select(name => new ConnectionName(name))]);
    }

    private static Result<Routing, DelegationError> RoutingIn(JsonElement section) =>
        !section.TryGetProperty(RoutingField, out var routing) ? Routing.Capacity
        : routing.ValueKind != JsonValueKind.String ? DelegationError.Malformed
        : routing.GetString() switch
        {
            "capacity" or "leastUsed" => Routing.Capacity,
            "roundRobin" => Routing.RoundRobin,
            _ => DelegationError.UnknownRouting,
        };

    private static Result<int, DelegationError> Whole(JsonElement section, string field, int fallback, int most, DelegationError invalid) =>
        !section.TryGetProperty(field, out var value) ? fallback
        : value.ValueKind != JsonValueKind.Number ? DelegationError.Malformed
        : value.TryGetInt32(out var whole) && whole >= 1 && whole <= most ? whole
        : invalid;
}
