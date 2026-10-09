using Avala.Agents.Contracts.Connections;
using Avala.ClaudeCode.Conversations;

namespace Avala.ClaudeCode.Discovery;

internal sealed class LoginFolders(UserHome home) : IConnectionDiscovery
{
    public const string LoginSource = "login";

    private const int LongestName = 64;

    private static readonly string[] LoginMarks = [".credentials.json", ".claude.json"];

    public ValueTask<IReadOnlyList<DiscoveredConnection>> DiscoverAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DiscoveredConnection> found =
        [
            .. Candidates()
                .Select(folder => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)))
                .Distinct(StringComparer.Ordinal)
                .Where(HoldsLogin)
                .Select(folder => new DiscoveredConnection(NameOf(folder), ClaudeCodeProvider.Id, new CredentialReference(LoginSource, folder)))
                .DistinctBy(connection => connection.Name)
                .OrderBy(connection => connection.Name.Value, StringComparer.Ordinal),
        ];

        return ValueTask.FromResult(found);
    }

    public bool HoldsAnyLogin() => Candidates().Any(HoldsLogin);

    public static ConnectionName NameOf(string folder)
    {
        var readable = new string([.. Path.GetFileName(folder).TrimStart('.')
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-')]);
        var name = readable.StartsWith("claude", StringComparison.Ordinal) ? readable : $"claude-{readable}";

        return new ConnectionName(name[..Math.Min(LongestName, name.Length)]);
    }

    private IEnumerable<string> Candidates() =>
        [
            home.DefaultFolder,
            .. Directory.Exists(home.Folder) ? Directory.EnumerateDirectories(home.Folder, ".claude-*") : [],
            .. home.Configured.Where(folder => !string.IsNullOrWhiteSpace(folder) && Path.IsPathFullyQualified(folder)),
        ];

    private bool HoldsLogin(string folder) =>
        Directory.Exists(folder)
        && (folder == home.DefaultFolder
            ? File.Exists(Path.Combine(home.Folder, ".claude.json")) || LoginMarks.Any(mark => File.Exists(Path.Combine(folder, mark)))
            : LoginMarks.Any(mark => File.Exists(Path.Combine(folder, mark))));
}
