namespace Avala.ClaudeCode.Conversations;

internal static class MainRepository
{
    private const string Pointer = "gitdir:";

    public static async Task<string> OfAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var marker = Path.Combine(workingDirectory, ".git");

        try
        {
            var text = File.Exists(marker) ? (await File.ReadAllTextAsync(marker, cancellationToken)).Trim() : string.Empty;

            return text.StartsWith(Pointer, StringComparison.Ordinal)
                ? Owner(Path.GetFullPath(text[Pointer.Length..].Trim(), workingDirectory), workingDirectory)
                : workingDirectory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return workingDirectory;
        }
    }

    private static string Owner(string gitDirectory, string workingDirectory)
    {
        var worktrees = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(gitDirectory));
        var common = Path.GetDirectoryName(worktrees ?? string.Empty);

        return Path.GetFileName(worktrees) == "worktrees" && Path.GetFileName(common) == ".git" && Path.GetDirectoryName(common) is { Length: > 0 } repository
            ? repository
            : workingDirectory;
    }
}
