using Avala.Agents.Contracts.Events;

namespace Avala.Permissions.Policies;

internal sealed record CommandLine(IReadOnlyList<string> Commands, IReadOnlyList<string> Writes, bool Opaque)
{
    public bool MovesDirectory { get; init; }

    public static CommandLine Parse(string text) => new ShellReader(text).Read();

    public static bool Rooted(string path) => path.StartsWith('/') || path.StartsWith('\\') || (path.Length > 1 && path[1] == ':');

    public PermissionRequest Written(string path, PermissionRequest request) =>
        MovesDirectory && !Rooted(path) ? new PermissionRequest(ItemKind.FileEdit, path, InsideWorkspace: false) : request.Locate(path);
}
