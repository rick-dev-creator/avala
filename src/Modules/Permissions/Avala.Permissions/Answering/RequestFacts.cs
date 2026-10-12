using Avala.Agents.Contracts.Events;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Answering;

internal static class RequestFacts
{
    extension(PermissionRequested requested)
    {
        public PermissionRequest Facts(Option<string> workingDirectory, IRealPaths paths) => requested.Facts(workingDirectory, Option<string>.None, paths);

        public PermissionRequest Facts(Option<string> workingDirectory, Option<string> scratch, IRealPaths paths) => requested.Kind switch
        {
            ItemKind.FileEdit => Edit(requested.Target, workingDirectory, scratch, paths),
            ItemKind.Command => new PermissionRequest(ItemKind.Command, requested.Target, InsideWorkspace: false)
            {
                Locate = written => Edit(written, workingDirectory, scratch, paths),
            },
            _ => new PermissionRequest(requested.Kind, requested.Target, InsideWorkspace: false),
        };
    }

    private static PermissionRequest Edit(string target, Option<string> workingDirectory, Option<string> scratch, IRealPaths paths) =>
        IsPath(target)
            ? workingDirectory.Match(root => Located(target, root, scratch, paths), () => Outside(target))
            : Outside(target);

    private static PermissionRequest Outside(string target) => new(ItemKind.FileEdit, target, InsideWorkspace: false);

    private static bool IsPath(string target) => !string.IsNullOrWhiteSpace(target) && !target.Contains('\0', StringComparison.Ordinal);

    private static PermissionRequest Located(string target, string root, Option<string> scratch, IRealPaths paths)
    {
        var workspace = Path.GetFullPath(root);
        var asked = Path.Combine(workspace, target).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var lexical = Path.GetFullPath(asked);
        var real = paths.Resolve(asked);
        var shown = real == paths.Resolve(lexical) ? lexical : asked;

        var located = paths.Resolve(workspace).Match(
            inside => real.Match(full => Within(inside, full, shown), () => Outside(shown)),
            () => Outside(shown));

        return located.InsideWorkspace ? located : Scratched(scratch, real, shown, paths).Match(found => found, () => located);
    }

    private static Option<PermissionRequest> Scratched(Option<string> scratch, Option<string> real, string shown, IRealPaths paths) =>
        scratch.Match(
            folder => paths.Resolve(Path.GetFullPath(folder)).Match(
                resolved => real.Match(
                    full => Contains(resolved, full) ? new PermissionRequest(ItemKind.FileEdit, shown, InsideWorkspace: true) : Option<PermissionRequest>.None,
                    () => Option<PermissionRequest>.None),
                () => Option<PermissionRequest>.None),
            () => Option<PermissionRequest>.None);

    private static PermissionRequest Within(string workspace, string full, string shown) =>
        Contains(workspace, full)
            ? new PermissionRequest(ItemKind.FileEdit, Path.GetRelativePath(workspace, full).Replace(Path.DirectorySeparatorChar, '/'), InsideWorkspace: true)
            : Outside(shown);

    private static bool Contains(string folder, string full)
    {
        var relative = Path.GetRelativePath(folder, full);

        return relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }
}
