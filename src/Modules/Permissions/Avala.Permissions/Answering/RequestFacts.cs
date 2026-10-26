using Avala.Agents.Contracts.Events;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Answering;

internal static class RequestFacts
{
    extension(PermissionRequested requested)
    {
        public PermissionRequest Facts(Option<string> workingDirectory, IRealPaths paths) => requested.Facts(workingDirectory, Option<string>.None, [], paths);

        public PermissionRequest Facts(Option<string> workingDirectory, Option<string> scratch, IReadOnlyList<string> readable, IRealPaths paths) =>
            requested.Facts(workingDirectory, scratch, readable, [], paths);

        public PermissionRequest Facts(Option<string> workingDirectory, Option<string> scratch, IReadOnlyList<string> readable, IReadOnlyList<string> editable, IRealPaths paths) =>
            requested.Facts(workingDirectory, scratch.Match<IReadOnlyList<string>>(folder => [folder], () => []), readable, editable, paths);

        public PermissionRequest Facts(Option<string> workingDirectory, IReadOnlyList<string> writable, IReadOnlyList<string> readable, IReadOnlyList<string> editable, IRealPaths paths) => requested.Kind switch
        {
            ItemKind.FileEdit => Granted(Edit(requested.Target, workingDirectory, writable, readable, paths), editable, paths),
            ItemKind.Command => new PermissionRequest(ItemKind.Command, requested.Target, InsideWorkspace: false)
            {
                Locate = written => Granted(Edit(written, workingDirectory, writable, readable, paths), editable, paths),
            },
            _ => new PermissionRequest(requested.Kind, requested.Target, InsideWorkspace: false)
            {
                Readable = requested.Reads.Match(read => Readable(read, readable, paths), () => false),
            },
        };
    }

    private static PermissionRequest Edit(string target, Option<string> workingDirectory, IReadOnlyList<string> writable, IReadOnlyList<string> readable, IRealPaths paths)
    {
        var located = Placed(target, workingDirectory, writable, readable, paths);

        if (target.IndexOfAny(['*', '?']) < 0 || !IsPath(target))
        {
            return located;
        }

        var pattern = Path.IsPathFullyQualified(target) ? target : workingDirectory.Map(root => Path.Combine(Path.GetFullPath(root), target)).Match(full => full, () => string.Empty);

        return (pattern.Length > 0 ? paths.Expand(pattern) : Option<IReadOnlyList<string>>.None).Match(
            matches => matches.Select(match => Placed(match, workingDirectory, writable, readable, paths)).ToList() is var placed && placed.TrueForAll(match => match.InsideWorkspace)
                ? located
                : Outside(located.Target) with { Readable = (located.InsideWorkspace || located.Readable) && placed.TrueForAll(match => match.InsideWorkspace || match.Readable) },
            () => Outside(located.Target));
    }

    private static PermissionRequest Placed(string target, Option<string> workingDirectory, IReadOnlyList<string> writable, IReadOnlyList<string> readable, IRealPaths paths)
    {
        var located = IsPath(target)
            ? workingDirectory.Match(root => Located(target, root, writable, paths), () => Outside(target))
            : Outside(target);

        return located.InsideWorkspace || !Readable(located.Target, readable, paths) ? located : located with { Readable = true };
    }

    private static bool Readable(string path, IReadOnlyList<string> readable, IRealPaths paths) =>
        readable.Count > 0
        && IsPath(path)
        && Path.IsPathFullyQualified(path)
        && paths.Resolve(Path.GetFullPath(path)).Match(
            real => readable.Where(folder => IsPath(folder) && Path.IsPathFullyQualified(folder))
                .Any(folder => paths.Resolve(Path.GetFullPath(folder)).Match(root => Contains(root, real), () => false)),
            () => false);

    private static PermissionRequest Granted(PermissionRequest located, IReadOnlyList<string> editable, IRealPaths paths)
    {
        if (located.InsideWorkspace || !IsPath(located.Target) || !Path.IsPathFullyQualified(located.Target) || located.Target.IndexOfAny(['*', '?']) >= 0)
        {
            return located;
        }

        return paths.Resolve(Path.GetFullPath(located.Target)).Match(
            real => located with
            {
                Granted = editable.FirstOrDefault(folder => Editable(folder, real, paths)).ToOption(),
                Grantable = paths.Repository(real).Bind(root => Guarded(root, real) ? Option<string>.None : root),
            },
            () => located);
    }

    private static bool Editable(string folder, string real, IRealPaths paths) =>
        IsPath(folder)
        && Path.IsPathFullyQualified(folder)
        && paths.Resolve(Path.GetFullPath(folder)).Match(root => Contains(root, real) && !Guarded(root, real), () => false);

    public static bool Within(string path, IReadOnlyList<string> folders, IRealPaths paths) => Readable(path, folders, paths);

    private static bool Guarded(string root, string real)
    {
        var relative = "/" + Path.GetRelativePath(root, real).Replace(Path.DirectorySeparatorChar, '/');

        return relative.Contains("/.git/", StringComparison.Ordinal) || relative.EndsWith("/.git", StringComparison.Ordinal) || relative.EndsWith("/" + PermissionPolicy.PolicyFile, StringComparison.Ordinal);
    }

    private static PermissionRequest Outside(string target) => new(ItemKind.FileEdit, target, InsideWorkspace: false);

    private static bool IsPath(string target) => !string.IsNullOrWhiteSpace(target) && !target.Contains('\0', StringComparison.Ordinal);

    private static PermissionRequest Located(string target, string root, IReadOnlyList<string> writable, IRealPaths paths)
    {
        var workspace = Path.GetFullPath(root);
        var asked = Path.Combine(workspace, target).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var lexical = Path.GetFullPath(asked);
        var real = paths.Resolve(asked);
        var shown = real == paths.Resolve(lexical) ? lexical : asked;

        var located = paths.Resolve(workspace).Match(
            inside => real.Match(full => Within(inside, full, shown), () => Outside(shown)),
            () => Outside(shown));

        return located.InsideWorkspace ? located : Written(writable, real, shown, paths).Match(found => found, () => located);
    }

    private static Option<PermissionRequest> Written(IReadOnlyList<string> writable, Option<string> real, string shown, IRealPaths paths) =>
        real.Match(
            full => writable.Any(folder => IsPath(folder) && Path.IsPathFullyQualified(folder) && paths.Resolve(Path.GetFullPath(folder)).Match(resolved => Contains(resolved, full) && !Guarded(resolved, full), () => false))
                ? new PermissionRequest(ItemKind.FileEdit, shown, InsideWorkspace: true)
                : Option<PermissionRequest>.None,
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
