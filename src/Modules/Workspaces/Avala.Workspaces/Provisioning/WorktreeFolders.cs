using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Provisioning;

internal static class WorktreeFolders
{
    extension(WorkspaceLocation location)
    {
        public bool IsAt(string folder) =>
            !string.IsNullOrWhiteSpace(folder)
            && string.Equals(Normalized(location.Path), Normalized(folder), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string Normalized(string folder) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
}
