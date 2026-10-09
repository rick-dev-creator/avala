namespace Avala.Workspaces.Git;

internal static class GitRefs
{
    public const string Heads = "refs/heads/";

    public static readonly string[] Pristine = ["--no-replace-objects", "--literal-pathspecs"];
}
