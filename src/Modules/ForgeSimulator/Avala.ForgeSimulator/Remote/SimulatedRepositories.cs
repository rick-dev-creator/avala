using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.ForgeSimulator.Remote;

internal sealed class SimulatedRepositories(IProcessRunner processes, AvalaPaths paths)
{
    public const string Owner = "avala";

    public string Root => paths.Folder("simulated-forge");

    public async Task<Result<HostedRepository, ForgeError>> CreateAsync(RepositoryDraft draft, CancellationToken cancellationToken)
    {
        var name = draft.Name.Trim();

        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return ForgeError.Rejected;
        }

        var owner = Directory.CreateDirectory(Path.Combine(Root, Owner)).FullName;
        var path = Path.Combine(owner, $"{name}.git");

        if (Directory.Exists(path))
        {
            return ForgeError.NameTaken;
        }

        var created = await processes.RunAsync(new ProcessRequest("git", ["init", "--quiet", "--bare", path]), cancellationToken);

        return created.Match(outcome => outcome.Succeeded, _ => false)
            ? Hosted(name, path)
            : ForgeError.GitFailed;
    }

    public IReadOnlyList<ListedRepository> List()
    {
        var owner = new DirectoryInfo(Path.Combine(Root, Owner));

        return owner.Exists
            ? [.. owner.EnumerateDirectories("*.git")
                .Select(folder => new ListedRepository(Hosted(folder.Name[..^".git".Length], folder.FullName), Private: true, Archived: false, Latest(folder)))
                .OrderByDescending(listed => listed.UpdatedAt)]
            : [];
    }

    private static DateTimeOffset Latest(DirectoryInfo folder)
    {
        var refs = new DirectoryInfo(Path.Combine(folder.FullName, "refs"));
        IEnumerable<DateTime> changes = refs.Exists
            ? [folder.LastWriteTimeUtc, refs.LastWriteTimeUtc, .. refs.EnumerateFiles("*", SearchOption.AllDirectories).Select(file => file.LastWriteTimeUtc)]
            : [folder.LastWriteTimeUtc];

        return new DateTimeOffset(changes.Max(), TimeSpan.Zero);
    }

    private static HostedRepository Hosted(string name, string path) =>
        new(new RepositoryAddress(string.Empty, Owner, name), new Uri(SimulatedForge.Site, $"{Owner}/{Uri.EscapeDataString(name)}"), path, path);
}
