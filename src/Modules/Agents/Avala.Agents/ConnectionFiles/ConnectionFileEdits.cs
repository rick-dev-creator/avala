using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Agents.Connections;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.ConnectionFiles;

internal static class ConnectionFileEdits
{
    private const string DefaultField = "default";

    private const string ConnectionsField = "connections";

    private static readonly JsonSerializerOptions Written = new() { WriteIndented = true };

    public static Result<string, ConnectionError> Apply(string text, IConnectionChange change)
    {
        if (!ConnectionFileParser.Parse(text).TryGetValue(out var declarations, out var error)
            || !change.ApplyTo(declarations).TryGetValue(out _, out error))
        {
            return error;
        }

        if (JsonNode.Parse(text) is not JsonObject root)
        {
            return ConnectionError.Malformed;
        }

        switch (change)
        {
            case DefaultChange chosen:
                root[DefaultField] = chosen.Connection.Match(name => name.Value, () => ConnectionDeclarations.Auto);
                break;
            case DeclarationChange declared:
                Declare(root, declared);
                break;
            case RemovalChange removed:
                Remove(root, removed.Name);
                break;
        }

        return root.ToJsonString(Written);
    }

    private static void Declare(JsonObject root, DeclarationChange declared)
    {
        var connections = Connections(root);
        var entry = declared.Replacing.Match(name => Find(connections, name), () => null) ?? Appended(connections);
        entry["name"] = declared.Name.Value;
        entry["provider"] = declared.Provider;
        Put(entry, "credential", declared.Credential.Map(Credential));

        foreach (var model in declared.Model.Match<ModelChoice[]>(chosen => [chosen], () => []))
        {
            Choose(entry, model);
        }

        if (declared.Replacing.Match(name => root[DefaultField]?.GetValue<string>() == name.Value, () => false))
        {
            root[DefaultField] = declared.Name.Value;
        }
    }

    private static JsonNode Credential(CredentialDeclaration credential)
    {
        var written = new JsonObject { ["source"] = credential.Source };
        Put(written, "reference", credential.Reference.Map(Value));

        return written;
    }

    private static void Choose(JsonObject entry, ModelChoice model)
    {
        if (entry["settings"] is not JsonObject settings)
        {
            settings = [];
            entry["settings"] = settings;
        }

        Put(settings, OffersModels.ModelSetting, model.Model.Map(Value));
        Put(settings, OffersModels.EffortSetting, model.Effort.Map(Value));

        if (settings.Count == 0)
        {
            entry.Remove("settings");
        }
    }

    private static void Put(JsonObject target, string name, Option<JsonNode> value)
    {
        target.Remove(name);

        foreach (var set in value.Match<JsonNode[]>(found => [found], () => []))
        {
            target[name] = set;
        }
    }

    private static JsonNode Value(string text) => JsonValue.Create(text);

    private static void Remove(JsonObject root, ConnectionName name)
    {
        var connections = Connections(root);

        if (Find(connections, name) is { } entry)
        {
            connections.Remove(entry);
        }

        if (connections.Count == 0)
        {
            root.Remove(ConnectionsField);
        }
    }

    private static JsonArray Connections(JsonObject root)
    {
        if (root[ConnectionsField] is JsonArray existing)
        {
            return existing;
        }

        var created = new JsonArray();
        root[ConnectionsField] = created;

        return created;
    }

    private static JsonObject? Find(JsonArray connections, ConnectionName name) =>
        connections.OfType<JsonObject>().FirstOrDefault(entry => entry["name"]?.GetValue<string>() == name.Value);

    private static JsonObject Appended(JsonArray connections)
    {
        var entry = new JsonObject();
        connections.Add(entry);

        return entry;
    }
}
