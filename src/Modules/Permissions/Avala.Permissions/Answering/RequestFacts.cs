using Avala.Agents.Contracts.Events;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Answering;

internal static class RequestFacts
{
    extension(PermissionRequested requested)
    {
        public PermissionRequest Facts(Option<string> workingDirectory) =>
            requested.Kind == ItemKind.FileEdit && IsPath(requested.Target)
                ? workingDirectory.Match(
                    root => Located(requested.Target, root),
                    () => new PermissionRequest(ItemKind.FileEdit, requested.Target, InsideWorkspace: false))
                : new PermissionRequest(requested.Kind, requested.Target, InsideWorkspace: false);
    }

    private static bool IsPath(string target) => !string.IsNullOrWhiteSpace(target) && !target.Contains('\0', StringComparison.Ordinal);

    private static PermissionRequest Located(string target, string root)
    {
        var workspace = Path.GetFullPath(root);
        var full = Path.GetFullPath(target, workspace);
        var relative = Path.GetRelativePath(workspace, full);
        var inside = relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);

        return inside
            ? new PermissionRequest(ItemKind.FileEdit, relative.Replace(Path.DirectorySeparatorChar, '/'), InsideWorkspace: true)
            : new PermissionRequest(ItemKind.FileEdit, full, InsideWorkspace: false);
    }
}
