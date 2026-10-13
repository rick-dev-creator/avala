using System.Text.Json;

namespace Avala.Sdk;

public static class JsonSections
{
    public const int FileDepth = 64;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = FileDepth, AllowDuplicateProperties = false };

    public static Result<T, TError> Read<T, TError>(string text, string name, int depth, TError malformed, Func<Option<JsonElement>, Result<T, TError>> read)
        where TError : struct, Enum
    {
        try
        {
            using var document = JsonDocument.Parse(text, Options);
            var root = document.RootElement;

            return root.ValueKind != JsonValueKind.Object ? malformed
                : !root.TryGetProperty(name, out var section) ? read(Option<JsonElement>.None)
                : DepthOf(section) > depth ? malformed
                : read(Option<JsonElement>.Some(section));
        }
        catch (JsonException)
        {
            return malformed;
        }
    }

    private static int DepthOf(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => 1 + element.EnumerateObject().Select(property => DepthOf(property.Value)).DefaultIfEmpty().Max(),
            JsonValueKind.Array => 1 + element.EnumerateArray().Select(DepthOf).DefaultIfEmpty().Max(),
            _ => 0,
        };
}
