namespace Avala.ClaudeCode.Protocol;

internal sealed record Places(string WorkingDirectory, string Plans)
{
    public const string PlansFolder = "plans";

    public bool HoldsPlan(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        && Path.GetRelativePath(Path.GetFullPath(Plans, WorkingDirectory), Path.GetFullPath(path, WorkingDirectory)) is var relative
        && relative != ".."
        && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !Path.IsPathRooted(relative);
}
