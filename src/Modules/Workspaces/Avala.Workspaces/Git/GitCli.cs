using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Git;

internal sealed class GitCli(IProcessRunner processes) : IGit
{
    private static readonly string[] CheckpointIdentity =
        ["-c", "user.name=Avala", "-c", "user.email=avala@localhost", "-c", "commit.gpgsign=false"];

    public Task<Result<string, WorkspaceFailure>> FindRepositoryRootAsync(string path, CancellationToken cancellationToken) =>
        RunAsync(["-C", path, "rev-parse", "--show-toplevel"], WorkspaceFailure.NotAGitRepository, cancellationToken)
            .MapAsync(output => Path.GetFullPath(output.Trim()));

    public async Task<Result<bool, WorkspaceFailure>> BranchExistsAsync(
        string repository,
        BranchName branch,
        CancellationToken cancellationToken) =>
        (await processes.RunAsync(
            new ProcessRequest("git", ["-C", repository, "rev-parse", "--verify", "--quiet", $"refs/heads/{branch.Value}"]),
            cancellationToken))
            .MapError(_ => WorkspaceFailure.GitFailed)
            .Map(outcome => outcome.Succeeded);

    public Task<Result<WorkspaceLocation, WorkspaceFailure>> AddWorktreeAsync(
        WorkspaceLocation location,
        BranchName branch,
        string baseRef,
        CancellationToken cancellationToken) =>
        RunAsync(["-C", location.Repository, "worktree", "add", "-b", branch.Value, location.Path, baseRef], WorkspaceFailure.GitFailed, cancellationToken)
            .MapAsync(_ => location);

    public Task<Result<CommitSha, WorkspaceFailure>> CommitAllAsync(
        WorkspaceLocation location,
        string label,
        CancellationToken cancellationToken) =>
        RunAsync(["-C", location.Path, "add", "--all"], WorkspaceFailure.GitFailed, cancellationToken)
            .BindAsync(_ => RunAsync(
                [.. CheckpointIdentity, "-C", location.Path, "commit", "--allow-empty", "--no-verify", "--quiet", "--message", label],
                WorkspaceFailure.GitFailed,
                cancellationToken))
            .BindAsync(_ => RunAsync(["-C", location.Path, "rev-parse", "HEAD"], WorkspaceFailure.GitFailed, cancellationToken))
            .BindAsync(output => CommitSha.Create(output).MapError(_ => WorkspaceFailure.GitFailed));

    public Task<Result<WorkspaceLocation, WorkspaceFailure>> RemoveWorktreeAsync(
        WorkspaceLocation location,
        BranchName branch,
        CancellationToken cancellationToken) =>
        RunAsync(["-C", location.Repository, "worktree", "remove", "--force", location.Path], WorkspaceFailure.GitFailed, cancellationToken)
            .BindAsync(_ => RunAsync(["-C", location.Repository, "branch", "-D", branch.Value], WorkspaceFailure.GitFailed, cancellationToken))
            .MapAsync(_ => location);

    private async Task<Result<string, WorkspaceFailure>> RunAsync(
        IReadOnlyList<string> arguments,
        WorkspaceFailure failure,
        CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest("git", arguments), cancellationToken))
            .MapError(_ => failure)
            .Bind<string>(outcome => outcome.Succeeded ? outcome.Output : failure);
}
