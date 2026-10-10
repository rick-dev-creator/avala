using System.Collections.Frozen;
using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Sdk;

namespace Avala.ClaudeCode.Protocol;

internal sealed record ToolUse(string Id, string Name, JsonObject Input)
{
    private const int TitleLength = 80;

    private const int DetailsLength = 16 * 1024;

    private const string Selected = "select:";

    private static readonly FrozenDictionary<string, Traits> Known = new Dictionary<string, Traits>
    {
        ["Edit"] = new(ItemKind.FileEdit, Gated: true, (input, _) => Replacement(input)),
        ["MultiEdit"] = new(ItemKind.FileEdit, Gated: true, (input, _) => string.Join("\n\n", input.Items("edits").Select(Replacement))),
        ["Write"] = new(ItemKind.FileEdit, Gated: true, Field("content")),
        ["NotebookEdit"] = new(ItemKind.FileEdit, Gated: true, Field("new_source")),
        ["Bash"] = new(ItemKind.Command, Gated: true, Field("command")),
        ["PowerShell"] = new(ItemKind.Command, Gated: true, Field("command")),
        ["Grep"] = new(ItemKind.Search, Gated: false, Searching) { Reads = true },
        ["Glob"] = new(ItemKind.Search, Gated: false, Searching) { Reads = true },
        ["LS"] = new(ItemKind.Search, Gated: false, Raw) { Reads = true },
        ["WebFetch"] = new(ItemKind.Web, Gated: true, (input, _) => Joined('\n', input.TextOr("url", string.Empty), input.TextOr("prompt", string.Empty))),
        ["WebSearch"] = new(ItemKind.Web, Gated: true, Field("query")),
        ["Task"] = new(ItemKind.Subagent, Gated: false, Field("prompt")),
        ["Agent"] = new(ItemKind.Subagent, Gated: false, Field("prompt")),
        ["Read"] = new(ItemKind.Other, Gated: false, (input, workingDirectory) => Relative(input.TextOr("file_path", string.Empty), workingDirectory)) { Reads = true },
        ["NotebookRead"] = new(ItemKind.Other, Gated: false, Raw) { Reads = true },
        ["ToolSearch"] = new(ItemKind.Other, Gated: false, Field("query")),
        ["TodoWrite"] = Free,
        ["TaskCreate"] = Free,
        ["TaskUpdate"] = Free,
        ["TaskList"] = Free,
        ["TaskGet"] = Free,
        ["Skill"] = Free,
        ["AskUserQuestion"] = Free,
        ["ExitPlanMode"] = Free,
        ["EnterPlanMode"] = Free,
        ["BashOutput"] = Free,
        ["TaskOutput"] = Free,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private Traits Tool => Known.GetValueOrDefault(Name) ?? new(Name.StartsWith("mcp__", StringComparison.Ordinal) ? ItemKind.Mcp : ItemKind.Other, Gated: true, Raw);

    public ItemKind Kind => Tool.Kind;

    public string Target(string workingDirectory) => Kind switch
    {
        ItemKind.FileEdit => Located(Input.TextOr("file_path", Input.TextOr("notebook_path", string.Empty)), workingDirectory),
        ItemKind.Command => Input.TextOr("command", Name),
        ItemKind.Search => Input.TextOr("pattern", Input.TextOr("path", Name)),
        ItemKind.Web => Input.TextOr("url", Input.TextOr("query", Name)),
        ItemKind.Subagent => Input.TextOr("description", Input.TextOr("subagent_type", Name)),
        _ when Name == "Read" => Located(Input.TextOr("file_path", string.Empty), workingDirectory),
        _ => Name,
    };

    public bool Gated => Tool.Gated;

    public bool ReadsOutside(string workingDirectory) =>
        Tool.Reads
        && Resolved(Input.TextOr("file_path", Input.TextOr("notebook_path", Input.TextOr("path", string.Empty))), workingDirectory)
            .Match(path => Outside(path, workingDirectory), () => true);

    public string Title(string workingDirectory) => Shorten(Kind switch
    {
        ItemKind.FileEdit => $"{(Name == "Write" ? "Write" : "Edit")} {Relative(Target(workingDirectory), workingDirectory)}",
        ItemKind.Command => $"Run {FirstLine(Target(workingDirectory))}",
        ItemKind.Search => $"Search {Target(workingDirectory)}",
        ItemKind.Web => Name == "WebSearch" ? $"Search the web for {Target(workingDirectory)}" : $"Fetch {Target(workingDirectory)}",
        ItemKind.Subagent => $"Subagent: {Target(workingDirectory)}",
        _ when Name == "Read" => $"Read {Relative(Input.TextOr("file_path", Name), workingDirectory)}",
        _ when Name == "ToolSearch" => Searched(Input.TextOr("query", string.Empty)),
        _ => Name,
    });

    private static string Searched(string query) =>
        query.StartsWith(Selected, StringComparison.Ordinal) ? $"Load {query[Selected.Length..]}" : $"Find tools for {query}";

    public bool WritesPlan(Places places) =>
        Name is "Write" or "Edit" && Input.Text("file_path").Match(places.HoldsPlan, () => false);

    public string Heading(Places places) => WritesPlan(places) ? "Write the plan" : Title(places.WorkingDirectory);

    public ItemKind KindIn(Places places) => WritesPlan(places) ? ItemKind.Other : Kind;

    public string Planned(string plan) => Name switch
    {
        "Write" => Input.TextOr("content", plan),
        _ => Input.TextOr("old_string", string.Empty) is { Length: > 0 } old ? plan.Replace(old, Input.TextOr("new_string", string.Empty), StringComparison.Ordinal) : plan,
    };

    public Option<string> Details(string workingDirectory)
    {
        var details = Tool.Details(Input, workingDirectory);

        return details.Length == 0 ? Option<string>.None : details.Length <= DetailsLength ? details : $"{details[..DetailsLength]}…";
    }

    private static Traits Free => new(ItemKind.Other, Gated: false, Raw);

    private static Func<JsonObject, string, string> Field(string name) => (input, _) => input.TextOr(name, string.Empty);

    private static string Raw(JsonObject input, string workingDirectory) => input.Count == 0 ? string.Empty : input.ToJsonString();

    private static string Searching(JsonObject input, string workingDirectory) =>
        Joined(' ', input.TextOr("pattern", string.Empty), Scope(input, workingDirectory), input.TextOr("glob", string.Empty));

    private static string Joined(char separator, params string[] parts) => string.Join(separator, parts.Where(part => part.Length > 0));

    private static string Replacement(JsonNode edit) =>
        string.Join('\n', [.. Lines(edit.TextOr("old_string", string.Empty), "- "), .. Lines(edit.TextOr("new_string", string.Empty), "+ ")]);

    private static IEnumerable<string> Lines(string text, string mark) =>
        text.Length == 0 ? [] : text.TrimEnd('\n').Split('\n').Select(line => mark + line);

    private static string Scope(JsonObject input, string workingDirectory) =>
        input.Text("path").Match(path => $"in {Relative(path, workingDirectory)}", () => string.Empty);

    private static string Located(string path, string workingDirectory) =>
        Resolved(path, workingDirectory).Match(resolved => resolved, () => path);

    private static Option<string> Resolved(string path, string workingDirectory) =>
        string.IsNullOrWhiteSpace(path) ? workingDirectory
        : path.Contains('\0', StringComparison.Ordinal) || !Path.IsPathFullyQualified(workingDirectory) || Path.IsPathRooted(path) && !Path.IsPathFullyQualified(path) ? Option<string>.None
        : Path.GetFullPath(path, workingDirectory);

    private static bool Outside(string path, string workingDirectory) =>
        Path.GetRelativePath(workingDirectory, path) is var relative
        && (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || Path.IsPathRooted(relative));

    private static string Relative(string path, string workingDirectory) =>
        Path.IsPathFullyQualified(path) ? Path.GetRelativePath(workingDirectory, path).Replace('\\', '/') : path;

    private static string FirstLine(string text) => text.Split('\n', 2)[0];

    private static string Shorten(string text) => text.Length <= TitleLength ? text : $"{text[..(TitleLength - 1)]}…";

    private sealed record Traits(ItemKind Kind, bool Gated, Func<JsonObject, string, string> Details)
    {
        public bool Reads { get; init; }
    }
}
