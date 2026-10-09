using System.Text.Json.Nodes;
using Avala.Sdk;

namespace Avala.ClaudeCode.Protocol;

internal static class Json
{
    extension(JsonNode node)
    {
        public Option<JsonNode> Field(string name) =>
            node is JsonObject owner && owner.TryGetPropertyValue(name, out var value) && value is not null ? value : Option<JsonNode>.None;

        public Option<string> Text(string name) =>
            node.Field(name).Bind(value => value is JsonValue text && text.TryGetValue<string>(out var read) ? read : Option<string>.None);

        public string TextOr(string name, string fallback) => node.Text(name).Match(text => text, () => fallback);

        public long Number(string name) =>
            node.Field(name).Match(value => value is JsonValue number && number.TryGetValue<long>(out var read) ? read : 0, () => 0L);

        public Option<double> Fraction(string name) =>
            node.Field(name).Bind(value => value is JsonValue number && number.TryGetValue<double>(out var read) ? read : Option<double>.None);

        public Option<decimal> Amount(string name) =>
            node.Field(name).Bind(value => value is JsonValue number && number.TryGetValue<decimal>(out var read) ? read : Option<decimal>.None);

        public bool Flag(string name) =>
            node.Field(name).Match(value => value is JsonValue flag && flag.TryGetValue<bool>(out var read) && read, () => false);

        public JsonObject Members(string name) =>
            node.Field(name).Match(value => value as JsonObject ?? [], () => []);

        public IReadOnlyList<JsonNode> Items(string name) =>
            node.Field(name).Match<IReadOnlyList<JsonNode>>(value => value is JsonArray items ? [.. items.OfType<JsonNode>()] : [], () => []);
    }

    public static JsonObject Copy(JsonObject source) => (JsonObject)source.DeepClone();
}
