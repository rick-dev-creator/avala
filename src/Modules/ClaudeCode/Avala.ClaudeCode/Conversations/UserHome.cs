using Avala.Sdk;

namespace Avala.ClaudeCode.Conversations;

internal sealed record UserHome(string Folder, IReadOnlyList<string> Configured)
{
    public string DefaultFolder => Path.Combine(Folder, ".claude");

    public Option<string> TodoTools { get; init; }

    public bool IsDefault(string configurationFolder) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(configurationFolder)),
            DefaultFolder,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
