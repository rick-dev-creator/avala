using System.Text;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Sdk;

namespace Avala.Forges.RuleFiles;

internal static class ForgeFileParser
{
    public const string File = "forges.json";

    public const int MaximumBytes = 64 * 1024;

    private const string EnvironmentSource = "env";

    private const string CliSource = "cli";

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 4, AllowDuplicateProperties = false };

    public static Result<ForgeSettings, ForgeError> Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes)
        {
            return ForgeError.TooLarge;
        }

        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return Settings(document.RootElement);
        }
        catch (JsonException)
        {
            return ForgeError.Malformed;
        }
    }

    private static Result<ForgeSettings, ForgeError> Settings(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ForgeError.Malformed;
        }

        if (root.EnumerateObject().Any(field => field.Name is not ("forges" or "pollSeconds")))
        {
            return ForgeError.UnknownField;
        }

        return Poll(root).Bind(poll => Forges(root).Map(forges => new ForgeSettings(forges, poll)));
    }

    private static Result<TimeSpan, ForgeError> Poll(JsonElement root) =>
        !root.TryGetProperty("pollSeconds", out var poll) ? ForgeSettings.DefaultPoll
        : poll.ValueKind != JsonValueKind.Number || !poll.TryGetInt32(out var seconds) ? ForgeError.Malformed
        : TimeSpan.FromSeconds(seconds) is var interval && interval >= ForgeSettings.ShortestPoll && interval <= ForgeSettings.LongestPoll ? interval
        : ForgeError.InvalidInterval;

    private static Result<IReadOnlyList<ForgeDeclaration>, ForgeError> Forges(JsonElement root)
    {
        if (!root.TryGetProperty("forges", out var listed))
        {
            return Result<IReadOnlyList<ForgeDeclaration>, ForgeError>.Success([]);
        }

        if (listed.ValueKind != JsonValueKind.Array)
        {
            return ForgeError.Malformed;
        }

        var declared = new List<ForgeDeclaration>();

        foreach (var item in listed.EnumerateArray())
        {
            if (!Declaration(item).TryGetValue(out var forge, out var error))
            {
                return error;
            }

            if (declared.Exists(known => known.Name == forge.Name))
            {
                return ForgeError.DuplicateName;
            }

            declared.Add(forge);
        }

        return declared;
    }

    private static Result<ForgeDeclaration, ForgeError> Declaration(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return ForgeError.Malformed;
        }

        if (item.EnumerateObject().Any(field => field.Name is not ("name" or "forge" or "url" or "credential")))
        {
            return ForgeError.UnknownField;
        }

        if (Text(item, "name") is not { } name || !ForgeNames.IsValid(name))
        {
            return ForgeError.InvalidName;
        }

        if (Text(item, "forge") is not { Length: > 0 } forge)
        {
            return ForgeError.MissingForge;
        }

        return Url(item).Bind(url => Credential(item).Map(credential => new ForgeDeclaration(new ForgeName(name), forge, url, credential.Source, credential.Reference)));
    }

    private static Result<Option<Uri>, ForgeError> Url(JsonElement item)
    {
        if (!item.TryGetProperty("url", out var url))
        {
            return Option<Uri>.None;
        }

        return url.ValueKind == JsonValueKind.String
            && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var parsed)
            && parsed.Scheme is "https" or "http"
            ? Option<Uri>.Some(parsed)
            : ForgeError.InvalidUrl;
    }

    private static Result<(CredentialSource Source, Option<string> Reference), ForgeError> Credential(JsonElement item)
    {
        if (!item.TryGetProperty("credential", out var credential))
        {
            return (CredentialSource.None, Option<string>.None);
        }

        if (credential.ValueKind != JsonValueKind.Object)
        {
            return ForgeError.Malformed;
        }

        if (credential.EnumerateObject().Any(field => field.Name is not ("source" or "reference")))
        {
            return ForgeError.UnknownField;
        }

        var reference = credential.TryGetProperty("reference", out var named) ? named : default;

        if (reference.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.String))
        {
            return ForgeError.Malformed;
        }

        var referenced = reference.ValueKind == JsonValueKind.String ? reference.GetString() ?? string.Empty : string.Empty;

        return Text(credential, "source") switch
        {
            EnvironmentSource when string.IsNullOrWhiteSpace(referenced) => ForgeError.MissingReference,
            EnvironmentSource => (CredentialSource.Environment, Option<string>.Some(referenced)),
            CliSource when reference.ValueKind != JsonValueKind.Undefined => ForgeError.UnknownField,
            CliSource => (CredentialSource.Cli, Option<string>.None),
            _ => ForgeError.UnknownSource,
        };
    }

    private static string? Text(JsonElement item, string field) =>
        item.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
