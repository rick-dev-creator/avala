using Avala.Agents.Contracts.Events;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Answering;

internal static class RequestFacts
{
    extension(PermissionRequested requested)
    {
        public PermissionRequest Facts(Option<string> workingDirectory, IRealPaths paths) => requested.Kind switch
        {
            ItemKind.FileEdit => Edit(requested.Target, workingDirectory, paths),
            ItemKind.Command => new PermissionRequest(ItemKind.Command, requested.Target, InsideWorkspace: false)
            {
                Locate = written => Edit(written, workingDirectory, paths),
            },
            _ => new PermissionRequest(requested.Kind, requested.Target, InsideWorkspace: false),
        };
    }

    private static PermissionRequest Edit(string target, Option<string> workingDirectory, IRealPaths paths) =>
        IsPath(target)
            ? workingDirectory.Match(root => Located(target, root, paths), () => Outside(target))
            : Outside(target);

    private static PermissionRequest Outside(string target) => new(ItemKind.FileEdit, target, InsideWorkspace: false);

    private static bool IsPath(string target) => !string.IsNullOrWhiteSpace(target) && !target.Contains('\0', StringComparison.Ordinal);

    private static PermissionRequest Located(string target, string root, IRealPaths paths)
    {
        var workspace = Path.GetFullPath(root);
        var asked = Path.Combine(workspace, target).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var lexical = Path.GetFullPath(asked);
        var real = paths.Resolve(asked);
        var shown = real == paths.Resolve(lexical) ? lexical : asked;

        return paths.Resolve(workspace).Match(
            inside => real.Match(full => Within(inside, full, shown), () => Outside(shown)),
            () => Outside(shown));
    }

    private static PermissionRequest Within(string workspace, string full, string shown)
    {
        var relative = Path.GetRelativePath(workspace, full);
        var inside = relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);

        return inside
            ? new PermissionRequest(ItemKind.FileEdit, relative.Replace(Path.DirectorySeparatorChar, '/'), InsideWorkspace: true)
            : Outside(shown);
    }
}
