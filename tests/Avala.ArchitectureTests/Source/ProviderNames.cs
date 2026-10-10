namespace Avala.ArchitectureTests.Source;

internal static class ProviderNames
{
    public static IReadOnlyDictionary<string, string[]> Owned { get; } = new Dictionary<string, string[]>
    {
        [Path.Combine("Modules", "ClaudeCode")] = ["claude-code", "CLAUDE_CONFIG_DIR", "ANTHROPIC_API_KEY", "stream-json", "mcp__"],
        [Path.Combine("Modules", "GitHub")] = ["CHANGES_REQUESTED", "check-runs", "mergeable_state"],
        [Path.Combine("Modules", "Gitea")] = ["REQUEST_CHANGES", "/api/v1"],
    };

    private const string DesignTimeData = "DesignViewModels.cs";

    public static IEnumerable<string> Leaks(string sourceDirectory, IEnumerable<SourceFile> files) =>
        files
            .Where(file => Path.GetFileName(file.Path) != DesignTimeData)
            .SelectMany(file => Owned
                .Where(owner => !Path.GetRelativePath(sourceDirectory, file.Path).StartsWith(owner.Key + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .SelectMany(owner => owner.Value.Where(name => file.Text.Contains(name, StringComparison.Ordinal)))
                .Select(name => $"{file.Path}: {name}"));
}
