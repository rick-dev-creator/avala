using System.Text.Json;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Folders;

internal static class FolderTool
{
    public const string Name = "request_folder";

    public static HarnessTool Definition { get; } = new(
        Name,
        "Ask a person, once, for a folder outside your worktree, such as another repository you need to read or update. Call it before you read or edit there. The person grants it for the rest of this job, read only or read and edit, or refuses, and you receive the answer. Once granted, reads and edits there no longer ask; the folder's .git and .avala/permissions.json still go to a person. Commands that run in that folder are still decided one by one.",
        """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The folder, as an absolute path or one relative to your worktree; ~ stands for the home folder." },
            "access": { "type": "string", "enum": ["read", "edit"], "description": "read to only read the folder, edit to read and change files in it." },
            "reason": { "type": "string", "description": "Why the job needs the folder, shown to the person." }
          },
          "required": ["path", "access", "reason"],
          "additionalProperties": false
        }
        """,
        ToolSurface.Executed);

    public static Option<FolderRequest> Read(string input, Option<string> workingDirectory)
    {
        try
        {
            using var document = JsonDocument.Parse(input);
            var root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object && Text(root, "path") is { Length: > 0 } path
                ? Located(path, workingDirectory).Bind(folder => Access(Text(root, "access")).Map(access => new FolderRequest(folder, access, Text(root, "reason").Trim())))
                : Option<FolderRequest>.None;
        }
        catch (JsonException)
        {
            return Option<FolderRequest>.None;
        }
    }

    public static ToolResult Malformed(ItemId item) =>
        new(item, "Refused: the input needs a path, an access of read or edit, and a reason.") { IsError = true };

    public static ToolResult NoJob(ItemId item) => new(item, "Refused: this session runs no job, so there is nothing to grant the folder for.");

    public static ToolResult Missing(ItemId item, string folder) => new(item, $"Refused: there is no folder at {folder}.") { IsError = true };

    public static ToolResult Yours(ItemId item, string folder) => new(item, $"{folder} is inside your worktree; you need no grant for it.");

    public static ToolResult Already(ItemId item, FolderRequest request) =>
        new(item, request.Access == FolderAccess.Edit
            ? $"Already granted: you may read and edit files in {request.Folder} for the rest of this job."
            : $"Already granted: you may read files in {request.Folder} for the rest of this job.");

    public static ToolResult Answered(ItemId item, FolderRequest request, PermissionReply reply) =>
        new(item, reply.Answer == PermissionAnswer.Deny
            ? $"Refused: the person did not grant {request.Folder}.{reply.Message.Match(message => $" They said: {message}", () => string.Empty)} Do not read or edit it; carry on without it or explain what you need."
            : reply.Remember == Remember.EditsInThisFolder
            ? $"Granted: you may read and edit files in {request.Folder} for the rest of this job. Its .git and .avala/permissions.json still go to a person, and commands there are still decided one by one."
            : $"Granted read only: you may read files in {request.Folder} for the rest of this job; edits there still ask.");

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static Option<FolderAccess> Access(string access) => access switch
    {
        "read" => FolderAccess.Read,
        "edit" => FolderAccess.Edit,
        _ => Option<FolderAccess>.None,
    };

    private static Option<string> Located(string path, Option<string> workingDirectory)
    {
        var home = path == "~" || path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length > 2 ? path[2..] : string.Empty)
            : path;

        if (home.Contains('\0', StringComparison.Ordinal))
        {
            return Option<string>.None;
        }

        return Path.IsPathFullyQualified(home)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(home))
            : workingDirectory.Map(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, home))));
    }
}
