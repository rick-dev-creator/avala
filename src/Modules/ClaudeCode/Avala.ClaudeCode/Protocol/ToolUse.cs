using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Sdk;

namespace Avala.ClaudeCode.Protocol;

internal sealed record ToolUse(string Id, string Name, JsonObject Input)
{
    private const int TitleLength = 80;

    private const int DetailsLength = 16 * 1024;

    private const string Selected = "select:";

    public ItemKind Kind => Name switch
    {
        "Edit" or "Write" or "MultiEdit" or "NotebookEdit" => ItemKind.FileEdit,
        "Bash" or "PowerShell" => ItemKind.Command,
        "Grep" or "Glob" or "LS" => ItemKind.Search,
        "WebFetch" or "WebSearch" => ItemKind.Web,
        "Task" or "Agent" => ItemKind.Subagent,
        _ when Name.StartsWith("mcp__", StringComparison.Ordinal) => ItemKind.Mcp,
        _ => ItemKind.Other,
    };

    public string Target(string workingDirectory) => Kind switch
    {
        ItemKind.FileEdit => Resolved(Input.TextOr("file_path", Input.TextOr("notebook_path", string.Empty)), workingDirectory),
        ItemKind.Command => Input.TextOr("command", Name),
        ItemKind.Search => Input.TextOr("pattern", Input.TextOr("path", Name)),
        ItemKind.Web => Input.TextOr("url", Input.TextOr("query", Name)),
        ItemKind.Subagent => Input.TextOr("description", Input.TextOr("subagent_type", Name)),
        _ when Name == "Read" => Resolved(Input.TextOr("file_path", string.Empty), workingDirectory),
        _ => Name,
    };

    public bool Gated => Name is not ("Read" or "Grep" or "Glob" or "LS" or "NotebookRead" or "TodoWrite" or "TaskCreate" or "TaskUpdate" or "TaskList" or "TaskGet" or "ToolSearch" or "Skill"
        or "AskUserQuestion" or "ExitPlanMode" or "EnterPlanMode" or "Task" or "Agent" or "BashOutput" or "TaskOutput");

    public bool ReadsOutside(string workingDirectory) =>
        Name is "Read" or "NotebookRead" or "Grep" or "Glob" or "LS"
        && Path.GetRelativePath(workingDirectory, Resolved(Input.TextOr("file_path", Input.TextOr("notebook_path", Input.TextOr("path", string.Empty))), workingDirectory)) is var relative
        && (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || Path.IsPathRooted(relative));

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
        var details = Name switch
        {
            "Edit" => Replacement(Input),
            "MultiEdit" => string.Join("\n\n", Input.Items("edits").Select(Replacement)),
            "Write" => Input.TextOr("content", string.Empty),
            "NotebookEdit" => Input.TextOr("new_source", string.Empty),
            "Bash" or "PowerShell" => Input.TextOr("command", string.Empty),
            "Grep" or "Glob" => string.Join(' ', new[] { Input.TextOr("pattern", string.Empty), Scope(Input, workingDirectory), Input.TextOr("glob", string.Empty) }.Where(part => part.Length > 0)),
            "WebFetch" => string.Join('\n', new[] { Input.TextOr("url", string.Empty), Input.TextOr("prompt", string.Empty) }.Where(part => part.Length > 0)),
            "WebSearch" => Input.TextOr("query", string.Empty),
            "Task" or "Agent" => Input.TextOr("prompt", string.Empty),
            "ToolSearch" => Input.TextOr("query", string.Empty),
            "Read" => Relative(Input.TextOr("file_path", string.Empty), workingDirectory),
            _ => Input.Count == 0 ? string.Empty : Input.ToJsonString(),
        };

        return details.Length == 0 ? Option<string>.None : details.Length <= DetailsLength ? details : $"{details[..DetailsLength]}…";
    }

    private static string Replacement(JsonNode edit) =>
        string.Join('\n', [.. Lines(edit.TextOr("old_string", string.Empty), "- "), .. Lines(edit.TextOr("new_string", string.Empty), "+ ")]);

    private static IEnumerable<string> Lines(string text, string mark) =>
        text.Length == 0 ? [] : text.TrimEnd('\n').Split('\n').Select(line => mark + line);

    private static string Scope(JsonObject input, string workingDirectory) =>
        input.Text("path").Match(path => $"in {Relative(path, workingDirectory)}", () => string.Empty);

    private static string Resolved(string path, string workingDirectory) =>
        string.IsNullOrWhiteSpace(path) || path.Contains('\0', StringComparison.Ordinal) ? workingDirectory : Path.GetFullPath(path, workingDirectory);

    private static string Relative(string path, string workingDirectory) =>
        Path.IsPathFullyQualified(path) ? Path.GetRelativePath(workingDirectory, path).Replace('\\', '/') : path;

    private static string FirstLine(string text) => text.Split('\n', 2)[0];

    private static string Shorten(string text) => text.Length <= TitleLength ? text : $"{text[..(TitleLength - 1)]}…";
}
