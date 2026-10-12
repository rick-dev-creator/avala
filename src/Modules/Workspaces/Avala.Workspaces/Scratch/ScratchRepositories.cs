using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Git;

namespace Avala.Workspaces.Scratch;

internal sealed class ScratchRepositories(GitRepositories git, ScratchSettings settings, TimeProvider clock) : IScratchRepositories
{
    private const int Attempts = 4;

    private static readonly RepositorySeed Seed = new([(".avala/jobs.json", "{ \"approval\": \"merge\" }\n")], LendsIdentity: true);

    private static readonly StringComparison Comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public string Folder => Path.GetFullPath(settings.Folder);

    public bool Holds(string repository) =>
        !string.IsNullOrWhiteSpace(repository)
        && Path.GetFullPath(repository.Trim()).StartsWith(Path.TrimEndingDirectorySeparator(Folder) + Path.DirectorySeparatorChar, Comparison);

    public async ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string instruction, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return WorkspaceFailure.FileUnwritable;
        }

        var day = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        Result<string, WorkspaceFailure> created = WorkspaceFailure.FolderExists;

        for (var attempt = 0; attempt < Attempts && Taken(created); attempt++)
        {
            created = await git.CreateAsync(Folder, ScratchName.Of(day, instruction, settings.Ids()), Seed, cancellationToken);
        }

        return created;
    }

    private static bool Taken(Result<string, WorkspaceFailure> created) =>
        created.Match(_ => false, failure => failure == WorkspaceFailure.FolderExists);
}
