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

    private static readonly string[] Fields = [ConnectionsField, RoutingField, DepthField, ChildrenField];

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
                        .Map(children => new DelegationRules(connections, routing, depth, children)))));
    }

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
        !section.TryGetProperty(RoutingField, out var routing) ? Routing.RoundRobin
        : routing.ValueKind != JsonValueKind.String ? DelegationError.Malformed
        : routing.GetString() switch
        {
            "roundRobin" => Routing.RoundRobin,
            "leastUsed" => Routing.LeastUsed,
            _ => DelegationError.UnknownRouting,
        };

    private static Result<int, DelegationError> Whole(JsonElement section, string field, int fallback, int most, DelegationError invalid) =>
        !section.TryGetProperty(field, out var value) ? fallback
        : value.ValueKind != JsonValueKind.Number ? DelegationError.Malformed
        : value.TryGetInt32(out var whole) && whole >= 1 && whole <= most ? whole
        : invalid;
}
