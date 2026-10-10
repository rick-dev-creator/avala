using System.Globalization;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.GitHub.Api;

internal static class Json
{
    public static Result<T, ForgeError> Read<T>(string body, Func<JsonElement, T> read)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            return read(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or UriFormatException or ArgumentNullException)
        {
            return ForgeError.Malformed;
        }
    }

    public static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    public static string Id(JsonElement element) =>
        element.GetProperty("id") is var id && id.ValueKind == JsonValueKind.Number ? id.GetInt64().ToString(CultureInfo.InvariantCulture) : id.GetString() ?? string.Empty;

    public static Option<int> Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : Option<int>.None;

    public static Option<Uri> Link(JsonElement element, string name) =>
        Uri.TryCreate(Text(element, name), UriKind.Absolute, out var link) ? link : Option<Uri>.None;
}
