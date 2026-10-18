using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.WorkingFiles;

internal static class RepositoryPaths
{
    public static Result<string, WorkspaceFailure> Contained(string root, string path)
    {
        var full = Path.GetFullPath(Path.Combine(root, path));
        var inside = Path.GetRelativePath(root, full);

        return Path.IsPathRooted(path) || inside.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(inside)
            ? WorkspaceFailure.OutsideRepository
            : full;
    }
}
