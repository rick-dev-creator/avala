using System.Collections.Immutable;
using System.Text.Json;

namespace Avala.Testing;

public sealed record LogEvent(
    DateTimeOffset At,
    string Level,
    string Category,
    string Template,
    string Message,
    string Exception,
    ImmutableDictionary<string, string> Properties)
{
    public string Property(string name) => Properties.GetValueOrDefault(name, string.Empty);
}

public static class LogEvents
{
    private static readonly ImmutableHashSet<string> Reserved = ["@t", "@l", "@mt", "@m", "@x", "SourceContext"];

    public static IReadOnlyList<LogEvent> Parse(string text) =>
        [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Parsed)];

    private static LogEvent Parsed(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;

        return new LogEvent(
            root.GetProperty("@t").GetDateTimeOffset(),
            Text(root, "@l"),
            Text(root, "SourceContext"),
            Text(root, "@mt"),
            Text(root, "@m"),
            Text(root, "@x"),
            root.EnumerateObject()
                .Where(property => !Reserved.Contains(property.Name))
                .ToImmutableDictionary(property => property.Name, property => property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText()));
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;
}
