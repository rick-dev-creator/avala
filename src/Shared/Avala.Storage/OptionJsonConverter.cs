using System.Text.Json;
using System.Text.Json.Serialization;
using Avala.Sdk;

namespace Avala.Storage;

internal sealed class OptionJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Option<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(OptionJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()))!;
}

internal sealed class OptionJsonConverter<T> : JsonConverter<Option<T>>
    where T : notnull
{
    public override bool HandleNull => true;

    public override Option<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType != JsonTokenType.Null && JsonSerializer.Deserialize<T>(ref reader, options) is { } value
            ? Option<T>.Some(value)
            : Option<T>.None;

    public override void Write(Utf8JsonWriter writer, Option<T> value, JsonSerializerOptions options)
    {
        if (value.IsSome)
        {
            JsonSerializer.Serialize(writer, value.Match(present => present, () => default!), options);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
