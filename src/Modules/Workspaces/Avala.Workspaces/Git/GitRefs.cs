using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Git;

internal static class GitRefs
{
    public const string Heads = "refs/heads/";

    public const string Merged = "refs/avala/merged/";

    public static string MergedOf(WorkspaceId workspace) => $"{Merged}{workspace.Value:N}";

    public static readonly string[] Pristine = ["--no-replace-objects", "--literal-pathspecs"];
}
