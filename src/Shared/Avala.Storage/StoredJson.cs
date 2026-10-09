using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avala.Storage;

public static class StoredJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(), new OptionJsonConverter() },
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Read<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"The stored {typeof(T).Name} is empty.");
}
