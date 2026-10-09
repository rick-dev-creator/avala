using Avala.Agents.Contracts.Connections;
using Avala.Sdk;
using Avala.Simulator.Playback;

namespace Avala.Simulator.FileSystem;

internal sealed class LoginDiscovery(AvalaPaths paths) : IConnectionDiscovery
{
    public const string FolderName = "simulated-logins";

    public const string Prefix = "simulator-";

    public const string LoginSource = "login";

    private const int LongestName = 64;

    public ValueTask<IReadOnlyList<DiscoveredConnection>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var folder = paths.Folder(FolderName);
        IReadOnlyList<DiscoveredConnection> found = Directory.Exists(folder)
            ? [.. Directory.GetDirectories(folder)
                .Select(Path.GetFullPath)
                .Order(StringComparer.Ordinal)
                .Select(login => new DiscoveredConnection(NameOf(login), SimulatedProvider.Id, new CredentialReference(LoginSource, login)))]
            : [];

        return ValueTask.FromResult(found);
    }

    public static ConnectionName NameOf(string login)
    {
        var readable = new string([.. Path.GetFileName(Path.TrimEndingDirectorySeparator(login))
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-')]);

        return new ConnectionName($"{Prefix}{readable}"[..Math.Min(LongestName, Prefix.Length + readable.Length)]);
    }
}
