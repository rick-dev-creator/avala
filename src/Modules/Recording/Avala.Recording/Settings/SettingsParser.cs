using System.Text.Json;
using Avala.Recording.Recordings;
using Avala.Sdk;

namespace Avala.Recording.Settings;

internal static class SettingsParser
{
    private const string Enabled = "enabled";

    private const string Redact = "redact";

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 2, AllowDuplicateProperties = false };

    public static Result<RecordingSettings, RecordingError> Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return Settings(document.RootElement);
        }
        catch (JsonException)
        {
            return RecordingError.Malformed;
        }
    }

    private static Result<RecordingSettings, RecordingError> Settings(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return RecordingError.Malformed;
        }

        if (root.EnumerateObject().Any(property => property.Name is not (Enabled or Redact)))
        {
            return RecordingError.UnknownField;
        }

        var enabled = root.TryGetProperty(Enabled, out var flag) ? flag.ValueKind switch
        {
            JsonValueKind.True => Option<bool>.Some(true),
            JsonValueKind.False => Option<bool>.Some(false),
            _ => Option<bool>.None,
        } : Option<bool>.Some(false);
        var redactions = root.TryGetProperty(Redact, out var secrets) ? Redactions(secrets) : Result<IReadOnlyList<string>, RecordingError>.Success([]);

        return enabled.Match(
            on => redactions.Map(listed => new RecordingSettings(on, listed)),
            () => RecordingError.Malformed);
    }

    private static Result<IReadOnlyList<string>, RecordingError> Redactions(JsonElement secrets) =>
        secrets.ValueKind != JsonValueKind.Array ? RecordingError.Malformed
        : secrets.EnumerateArray().Any(secret => secret.ValueKind != JsonValueKind.String) ? RecordingError.Malformed
        : secrets.EnumerateArray().Any(secret => string.IsNullOrWhiteSpace(secret.GetString())) ? RecordingError.InvalidRedaction
        : Result<IReadOnlyList<string>, RecordingError>.Success([.. secrets.EnumerateArray().Select(secret => secret.GetString()!)]);
}
