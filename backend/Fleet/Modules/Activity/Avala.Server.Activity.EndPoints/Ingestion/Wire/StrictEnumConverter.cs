using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avala.Server.Activity.EndPoints.Ingestion.Wire;

internal sealed class StrictEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    private readonly FrozenDictionary<string, T> names =
        Enum.GetValues<T>().ToFrozenDictionary(value => JsonNamingPolicy.CamelCase.ConvertName(value.ToString()), StringComparer.Ordinal);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && names.TryGetValue(reader.GetString() ?? string.Empty, out var value)
            ? value
            : throw new JsonException($"Expected one of: {string.Join(", ", names.Keys.Order(StringComparer.Ordinal))}.");

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}
