using System.Text.Json;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Supervision.Watching;

namespace Avala.Supervision.Settings;

internal static class SettingsParser
{
    private const string Silence = "silenceSeconds";

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 2, AllowDuplicateProperties = false };

    public static Result<TimeSpan, SupervisionError> Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return Window(document.RootElement);
        }
        catch (JsonException)
        {
            return SupervisionError.Malformed;
        }
    }

    private static Result<TimeSpan, SupervisionError> Window(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return SupervisionError.Malformed;
        }

        if (root.EnumerateObject().Any(property => property.Name != Silence))
        {
            return SupervisionError.UnknownField;
        }

        if (!root.TryGetProperty(Silence, out var seconds))
        {
            return JobWatch.DefaultSilence;
        }

        if (seconds.ValueKind != JsonValueKind.Number || !seconds.TryGetDouble(out var value))
        {
            return SupervisionError.Malformed;
        }

        return value > 0 && value <= JobWatch.LongestSilence.TotalSeconds
            ? TimeSpan.FromSeconds(value)
            : SupervisionError.InvalidSilence;
    }
}
