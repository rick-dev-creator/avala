using System.Text.Json;
using System.Text.Json.Serialization;
using Avala.Sdk;

namespace Avala.Host.Tests;

internal static class DogfoodJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new OptionWriter(), new JsonStringEnumConverter() },
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        MaxDepth = 16,
    };

    public static string Show(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        try
        {
            return $"{value.GetType().Name} {JsonSerializer.Serialize(value, value.GetType(), Options)}";
        }
        catch (Exception failure) when (failure is NotSupportedException or JsonException or InvalidOperationException or ArgumentException)
        {
            return value.ToString() ?? string.Empty;
        }
    }

    private sealed class OptionWriter : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Option<>);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(OptionWriter<>).MakeGenericType(typeToConvert.GetGenericArguments()))!;
    }

    private sealed class OptionWriter<T> : JsonConverter<Option<T>>
        where T : notnull
    {
        public override Option<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("The dogfood journal only writes.");

        public override void Write(Utf8JsonWriter writer, Option<T> value, JsonSerializerOptions options)
        {
            if (value.Match<object?>(found => found, () => null) is { } found)
            {
                JsonSerializer.Serialize(writer, found, found.GetType(), options);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
