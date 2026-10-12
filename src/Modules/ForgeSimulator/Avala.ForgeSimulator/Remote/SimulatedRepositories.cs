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
            ? new HostedRepository(new RepositoryAddress(string.Empty, Owner, name), new Uri(SimulatedForge.Site, $"{Owner}/{Uri.EscapeDataString(name)}"), path, path)
            : ForgeError.GitFailed;
    }
}
