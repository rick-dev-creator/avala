using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Provisioning;

namespace Avala.Workspaces.WorkingFiles;

internal sealed class WorkingCommitter(IGit git, IProcessRunner processes) : IWorkingCommits
{
    public async ValueTask<Result<WorkingCommit, WorkspaceFailure>> CommitAsync(string repository, string path, string message, CancellationToken cancellationToken)
    {
        if (!(await git.FindRepositoryRootAsync(repository, cancellationToken)).TryGetValue(out var root, out var failure)
            || !RepositoryPaths.Contained(root, path).TryGetValue(out _, out failure))
        {
            return failure;
        }

        var branch = await RunAsync(root, ["symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken);

        if (!branch.Succeeded)
        {
            return WorkspaceFailure.NoBaseBranch;
        }

        if ((await RunAsync(root, ["status", "--porcelain", "--untracked-files=all", "--", path], cancellationToken)).Output.Trim().Length == 0)
        {
            return WorkspaceFailure.FileUnchanged;
        }

        if (!await HasIdentityAsync(root, cancellationToken))
        {
            return WorkspaceFailure.NoGitIdentity;
        }

        if (!(await RunAsync(root, ["add", "--", path], cancellationToken)).Succeeded
            || !(await RunAsync(root, ["commit", "--quiet", "--message", message, "--", path], cancellationToken)).Succeeded)
        {
            return WorkspaceFailure.GitFailed;
        }

        var head = await RunAsync(root, ["rev-parse", "HEAD"], cancellationToken);

        return head.Succeeded ? new WorkingCommit(head.Output.Trim(), branch.Output.Trim()) : WorkspaceFailure.GitFailed;
    }

    private async Task<bool> HasIdentityAsync(string root, CancellationToken cancellationToken)
    {
        var named = await RunAsync(root, ["config", "--get", "user.name"], cancellationToken);
        var mailed = await RunAsync(root, ["config", "--get", "user.email"], cancellationToken);

        return named.Succeeded && named.Output.Trim().Length > 0 && mailed.Succeeded && mailed.Output.Trim().Length > 0;
    }

    private async Task<(bool Succeeded, string Output)> RunAsync(string root, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest("git", ["-C", root, .. arguments]), cancellationToken))
            .Match(outcome => (outcome.Succeeded, outcome.Output), _ => (false, string.Empty));
}
