using System.Collections.Immutable;
using System.Text.Json;
using Avala.Agents.Connections;
using Avala.Agents.Contracts.Connections;
using Avala.Sdk;

namespace Avala.Agents.ConnectionFiles;

internal static class ConnectionFileParser
{
    public const int LongestName = 64;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 4, AllowDuplicateProperties = false };

    private static readonly string[] Sections = ["default", "connections"];

    private static readonly string[] Fields = ["name", "provider", "credential", "settings"];

    private static readonly string[] CredentialFields = ["source", "reference"];

    public static Result<ConnectionDeclarations, ConnectionError> Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return Declarations(document.RootElement);
        }
        catch (JsonException)
        {
            return ConnectionError.Malformed;
        }
    }

    public static bool IsValidName(string name) =>
        name.Length is > 0 and <= LongestName
        && char.IsAsciiLetterOrDigit(name[0])
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static Result<ConnectionDeclarations, ConnectionError> Declarations(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ConnectionError.Malformed;
        }

        if (!Known(root, Sections))
        {
            return ConnectionError.UnknownField;
        }

        if (!Connections(root).TryGetValue(out var connections, out var error)
            || !Text(root, "default").TryGetValue(out var named, out error))
        {
            return error;
        }

        var fallback = connections[0].Name;
        var chosen = named.Match(name => new ConnectionName(name), () => fallback);

        return connections.Any(connection => connection.Name == chosen)
            ? new ConnectionDeclarations(connections, chosen)
            : ConnectionError.UnknownDefault;
    }

    private static Result<IReadOnlyList<ConnectionDeclaration>, ConnectionError> Connections(JsonElement root)
    {
        if (!root.TryGetProperty("connections", out var connections))
        {
            return ConnectionError.NoConnections;
        }

        if (connections.ValueKind != JsonValueKind.Array)
        {
            return ConnectionError.Malformed;
        }

        var parsed = new List<ConnectionDeclaration>();

        foreach (var element in connections.EnumerateArray())
        {
            if (!Connection(element).TryGetValue(out var connection, out var error))
            {
                return error;
            }

            if (parsed.Any(earlier => earlier.Name == connection.Name))
            {
                return ConnectionError.DuplicateName;
            }

            parsed.Add(connection);
        }

        return parsed.Count == 0 ? ConnectionError.NoConnections : parsed;
    }

    private static Result<ConnectionDeclaration, ConnectionError> Connection(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return ConnectionError.Malformed;
        }

        if (!Known(element, Fields))
        {
            return ConnectionError.UnknownField;
        }

        if (!Text(element, "name").TryGetValue(out var name, out var error)
            || !Text(element, "provider").TryGetValue(out var provider, out error)
            || !Credential(element).TryGetValue(out var credential, out error)
            || !Settings(element).TryGetValue(out var settings, out error))
        {
            return error;
        }

        if (!name.Match(IsValidName, () => false))
        {
            return ConnectionError.InvalidName;
        }

        if (!provider.Match(id => !string.IsNullOrWhiteSpace(id), () => false))
        {
            return ConnectionError.MissingProvider;
        }

        return new ConnectionDeclaration(
            new ConnectionName(name.Match(value => value, () => string.Empty)),
            provider.Match(value => value, () => string.Empty))
        {
            Credential = credential,
            Settings = settings,
        };
    }

    private static Result<Option<CredentialDeclaration>, ConnectionError> Credential(JsonElement element)
    {
        if (!element.TryGetProperty("credential", out var credential))
        {
            return Option<CredentialDeclaration>.None;
        }

        if (credential.ValueKind != JsonValueKind.Object)
        {
            return ConnectionError.Malformed;
        }

        if (!Known(credential, CredentialFields))
        {
            return ConnectionError.UnknownField;
        }

        if (!Text(credential, "source").TryGetValue(out var source, out var error)
            || !Text(credential, "reference").TryGetValue(out var reference, out error))
        {
            return error;
        }

        if (!source.Match(kind => !string.IsNullOrWhiteSpace(kind), () => false))
        {
            return ConnectionError.MissingSource;
        }

        if (reference.Match(string.IsNullOrWhiteSpace, () => false))
        {
            return ConnectionError.MissingReference;
        }

        return Option<CredentialDeclaration>.Some(new CredentialDeclaration(source.Match(kind => kind, () => string.Empty), reference));
    }

    private static Result<IReadOnlyDictionary<string, string>, ConnectionError> Settings(JsonElement element)
    {
        if (!element.TryGetProperty("settings", out var settings))
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        if (settings.ValueKind != JsonValueKind.Object
            || settings.EnumerateObject().Any(setting => setting.Value.ValueKind != JsonValueKind.String || setting.Name.Length == 0))
        {
            return ConnectionError.Malformed;
        }

        return settings.EnumerateObject().ToImmutableDictionary(setting => setting.Name, setting => setting.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    private static Result<Option<string>, ConnectionError> Text(JsonElement element, string field) =>
        !element.TryGetProperty(field, out var value) ? Option<string>.None
        : value.ValueKind == JsonValueKind.String ? Option<string>.Some(value.GetString() ?? string.Empty)
        : ConnectionError.Malformed;

    private static bool Known(JsonElement element, string[] fields) =>
        element.EnumerateObject().All(property => fields.Contains(property.Name, StringComparer.Ordinal));
}
