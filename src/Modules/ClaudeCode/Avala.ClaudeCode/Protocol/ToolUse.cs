using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;

namespace Avala.ClaudeCode.Protocol;

internal sealed record ToolUse(string Id, string Name, JsonObject Input)
{
    private const int TitleLength = 80;

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

    public bool Gated => Name is not ("Read" or "Grep" or "Glob" or "LS" or "NotebookRead" or "TodoWrite" or "ToolSearch" or "Skill"
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
        _ => Name,
    });

    private static string Resolved(string path, string workingDirectory) =>
        string.IsNullOrWhiteSpace(path) || path.Contains('\0', StringComparison.Ordinal) ? workingDirectory : Path.GetFullPath(path, workingDirectory);

    private static string Relative(string path, string workingDirectory) =>
        Path.IsPathFullyQualified(path) ? Path.GetRelativePath(workingDirectory, path).Replace('\\', '/') : path;

    private static string FirstLine(string text) => text.Split('\n', 2)[0];

    private static string Shorten(string text) => text.Length <= TitleLength ? text : $"{text[..(TitleLength - 1)]}…";
}
