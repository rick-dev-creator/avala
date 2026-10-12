namespace Avala.Workspaces.Git;

internal sealed record RepositorySeed(IReadOnlyList<(string Path, string Content)> Files, bool LendsIdentity);
