using System.Globalization;
using System.Text;
using System.Text.Json;
using Avala.Sdk;
using Avala.Triggers.Contracts;

namespace Avala.Triggers.Declarations;

internal sealed record TemplateValues(TriggerId Trigger, string Repository, DateTimeOffset LocalTime, Option<JsonElement> Payload);

internal sealed record InstructionTemplate(string Text)
{
    public const int LongestValue = 4000;

    private const string Open = "{{";

    private const string Close = "}}";

    private const string PayloadRoot = "payload";

    private static readonly string[] Fixed = ["trigger.id", "trigger.repository", "run.at"];

    public static Option<InstructionTemplate> Parse(string text) =>
        Split(text).Match(found => found.Where(part => part.Placeholder).All(part => Known(part.Text)), () => false)
            ? new InstructionTemplate(text)
            : Option<InstructionTemplate>.None;

    public string Render(TemplateValues values)
    {
        var rendered = new StringBuilder();

        foreach (var (text, placeholder) in Split(Text).Match(found => found, () => [(Text, false)]))
        {
            rendered.Append(placeholder ? Cut(ValueOf(text, values)) : text);
        }

        return rendered.ToString();
    }

    private static Option<IReadOnlyList<(string Text, bool Placeholder)>> Split(string text)
    {
        var parts = new List<(string Text, bool Placeholder)>();
        var position = 0;

        while (position < text.Length)
        {
            var open = text.IndexOf(Open, position, StringComparison.Ordinal);

            if (open < 0)
            {
                parts.Add((text[position..], false));
                break;
            }

            var close = text.IndexOf(Close, open + Open.Length, StringComparison.Ordinal);

            if (close < 0)
            {
                return Option<IReadOnlyList<(string Text, bool Placeholder)>>.None;
            }

            parts.Add((text[position..open], false));
            parts.Add((text[(open + Open.Length)..close].Trim(), true));
            position = close + Close.Length;
        }

        return parts;
    }

    private static bool Known(string name) =>
        Fixed.Contains(name, StringComparer.Ordinal)
        || name == PayloadRoot
        || (name.StartsWith(PayloadRoot + ".", StringComparison.Ordinal) && name[(PayloadRoot.Length + 1)..].Split('.').All(Segment));

    private static bool Segment(string segment) =>
        segment.Length > 0 && !segment.Any(character => char.IsWhiteSpace(character) || character is '{' or '}');

    private static string ValueOf(string name, TemplateValues values) =>
        name switch
        {
            "trigger.id" => values.Trigger.Name,
            "trigger.repository" => values.Repository,
            "run.at" => values.LocalTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            _ => values.Payload.Bind(payload => Find(payload, name == PayloadRoot ? [] : name[(PayloadRoot.Length + 1)..].Split('.'))).Match(Inserted, () => string.Empty),
        };

    private static Option<JsonElement> Find(JsonElement root, IReadOnlyList<string> path)
    {
        var current = root;

        foreach (var segment in path)
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out var property))
            {
                current = property;
            }
            else if (current.ValueKind == JsonValueKind.Array
                && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index < current.GetArrayLength())
            {
                current = current[index];
            }
            else
            {
                return Option<JsonElement>.None;
            }
        }

        return current;
    }

    private static string Inserted(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();

    private static string Cut(string value) => value.Length > LongestValue ? value[..LongestValue] : value;
}
