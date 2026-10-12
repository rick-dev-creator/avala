using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Git;

internal sealed class GitRepositories(IProcessRunner processes) : IRepositories
{
    private const string FirstCommit = "Initial commit";

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

    public async ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string parent, string name, CancellationToken cancellationToken)
    {
        var named = name.Trim();

        if (!IsPlainName(named))
        {
            return WorkspaceFailure.InvalidRepositoryName;
        }

        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent.Trim()))
        {
            return WorkspaceFailure.FolderMissing;
        }

        var path = Path.Combine(Path.GetFullPath(parent.Trim()), named);

        if (Directory.Exists(path) || File.Exists(path))
        {
            return WorkspaceFailure.FolderExists;
        }

        if (!await HasIdentityAsync(Path.GetDirectoryName(path)!, cancellationToken))
        {
            return WorkspaceFailure.NoGitIdentity;
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return WorkspaceFailure.FileUnwritable;
        }

        return (await RunAsync(path, ["init", "--quiet"], cancellationToken)).Succeeded
            ? await CommitAsync(path, cancellationToken)
            : WorkspaceFailure.GitFailed;
    }

    private static bool IsPlainName(string name) =>
        name.Length > 0
        && name is not "." and not ".."
        && name.IndexOfAny(['/', '\\']) < 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private async Task<Result<string, WorkspaceFailure>> CommitAsync(string repository, CancellationToken cancellationToken)
    {
        var committed = (await RunAsync(repository, ["add", "--all"], cancellationToken)).Succeeded
            && (await RunAsync(repository, ["commit", "--allow-empty", "--quiet", "--message", FirstCommit], cancellationToken)).Succeeded;

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
