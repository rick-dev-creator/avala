using System.Text.Json;
using System.Text.Json.Serialization;
using Avala.Sdk;

namespace Avala.Recording.Storage;

internal sealed class OptionalTextConverter : JsonConverter<Option<string>>
{
    public override bool HandleNull => true;

    public override Option<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() is { } text ? text : Option<string>.None;

    public override void Write(Utf8JsonWriter writer, Option<string> value, JsonSerializerOptions options) =>
        value.Match<Action>(text => () => writer.WriteStringValue(text), () => writer.WriteNullValue)();
}
