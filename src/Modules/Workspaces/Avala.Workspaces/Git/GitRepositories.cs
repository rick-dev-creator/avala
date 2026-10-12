using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Git;

internal sealed class GitRepositories(IProcessRunner processes) : IRepositories
{
    private const string FirstCommit = "Initial commit";

    private static readonly string[] LentIdentity = ["-c", "user.name=Avala", "-c", "user.email=avala@localhost", "-c", "commit.gpgsign=false"];

    private static readonly RepositorySeed Empty = new([], LendsIdentity: false);

    public async ValueTask<RepositoryState> InspectAsync(string folder, CancellationToken cancellationToken)
    {
        var path = folder.Trim();

        if (path.Length == 0 || !Directory.Exists(path))
        {
            return RepositoryState.Missing;
        }

        if (!(await RunAsync(path, ["rev-parse", "--show-toplevel"], cancellationToken)).Succeeded)
        {
            return RepositoryState.NotARepository;
        }

        return (await RunAsync(path, ["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken)).Succeeded
            ? RepositoryState.Ready
            : RepositoryState.NoCommit;
    }

    public async ValueTask<Result<string, WorkspaceFailure>> InitializeAsync(string folder, CancellationToken cancellationToken)
    {
        var state = await InspectAsync(folder, cancellationToken);

        if (state is RepositoryState.Missing)
        {
            return WorkspaceFailure.FolderMissing;
        }

        if (state is RepositoryState.Ready)
        {
            return WorkspaceFailure.AlreadyARepository;
        }

        var path = Path.GetFullPath(folder.Trim());

        if (!await HasIdentityAsync(path, cancellationToken))
        {
            return WorkspaceFailure.NoGitIdentity;
        }

        if (state is RepositoryState.NoCommit)
        {
            var root = await RunAsync(path, ["rev-parse", "--show-toplevel"], cancellationToken);

            return root.Succeeded ? await CommitAsync(Path.GetFullPath(root.Output.Trim()), cancellationToken) : WorkspaceFailure.GitFailed;
        }

        return (await RunAsync(path, ["init", "--quiet"], cancellationToken)).Succeeded
            ? await CommitAsync(path, cancellationToken)
            : WorkspaceFailure.GitFailed;
    }

    public ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string parent, string name, CancellationToken cancellationToken) =>
        CreateAsync(parent, name, Empty, cancellationToken);

    public async ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string parent, string name, RepositorySeed seed, CancellationToken cancellationToken)
    {
        if (!Destination(parent, name).TryGetValue(out var path, out var refused))
        {
            return refused;
        }

        var known = await HasIdentityAsync(Path.GetDirectoryName(path)!, cancellationToken);

        if (!known && !seed.LendsIdentity)
        {
            return WorkspaceFailure.NoGitIdentity;
        }

        if (!await PlantAsync(path, seed, cancellationToken))
        {
            return WorkspaceFailure.FileUnwritable;
        }

        return (await RunAsync(path, ["init", "--quiet"], cancellationToken)).Succeeded
            ? await CommitAsync(path, known ? [] : LentIdentity, cancellationToken)
            : WorkspaceFailure.GitFailed;
    }

    private static Result<string, WorkspaceFailure> Destination(string parent, string name)
    {
        var named = name.Trim();

        if (!OwnedFolders.IsPlainName(named))
        {
            return WorkspaceFailure.InvalidRepositoryName;
        }

        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent.Trim()))
        {
            return WorkspaceFailure.FolderMissing;
        }

        var path = Path.Combine(Path.GetFullPath(parent.Trim()), named);

        return Directory.Exists(path) || File.Exists(path) ? WorkspaceFailure.FolderExists : path;
    }

    private static async Task<bool> PlantAsync(string path, RepositorySeed seed, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(path);

            foreach (var (file, content) in seed.Files)
            {
                var target = Path.Combine(path, file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllTextAsync(target, content, cancellationToken);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private Task<Result<string, WorkspaceFailure>> CommitAsync(string repository, CancellationToken cancellationToken) =>
        CommitAsync(repository, [], cancellationToken);

    private async Task<Result<string, WorkspaceFailure>> CommitAsync(string repository, IReadOnlyList<string> identity, CancellationToken cancellationToken)
    {
        var committed = (await RunAsync(repository, ["add", "--all"], cancellationToken)).Succeeded
            && (await RunAsync(repository, [.. identity, "commit", "--allow-empty", "--quiet", "--message", FirstCommit], cancellationToken)).Succeeded;

        return committed ? repository : WorkspaceFailure.GitFailed;
    }

    private async Task<bool> HasIdentityAsync(string folder, CancellationToken cancellationToken)
    {
        var named = await RunAsync(folder, ["config", "--get", "user.name"], cancellationToken);
        var mailed = await RunAsync(folder, ["config", "--get", "user.email"], cancellationToken);

        return named.Succeeded && named.Output.Trim().Length > 0 && mailed.Succeeded && mailed.Output.Trim().Length > 0;
    }

    private async Task<(bool Succeeded, string Output)> RunAsync(string folder, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest("git", ["-C", folder, .. arguments]), cancellationToken))
            .Match(outcome => (outcome.Succeeded, outcome.Output), _ => (false, string.Empty));
}
