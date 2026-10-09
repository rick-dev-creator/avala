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

    private static readonly string[] Pristine = GitRefs.Pristine;

    public Task<Result<string, WorkspaceFailure>> FindRepositoryRootAsync(string path, CancellationToken cancellationToken) =>
        RunAsync(["-C", path, "rev-parse", "--show-toplevel"], WorkspaceFailure.NotAGitRepository, cancellationToken)
            .MapAsync(output => Path.GetFullPath(output.Trim()));

    public Task<Result<CommitSha, WorkspaceFailure>> ResolveCommitAsync(string repository, string reference, CancellationToken cancellationToken) =>
        RunAsync(["-C", repository, "rev-parse", "--verify", "--quiet", "--end-of-options", $"{reference}^{{commit}}"], WorkspaceFailure.GitFailed, cancellationToken)
            .BindAsync(output => CommitSha.Create(output).MapError(_ => WorkspaceFailure.GitFailed));

    public async Task<Option<BranchName>> BranchOfAsync(string repository, string reference, CancellationToken cancellationToken) =>
        (await RunAsync(["-C", repository, "rev-parse", "--verify", "--quiet", "--symbolic-full-name", "--end-of-options", reference], WorkspaceFailure.GitFailed, cancellationToken))
            .Match(
                output => output.Trim() is var name && name.StartsWith(GitRefs.Heads, StringComparison.Ordinal)
                    ? BranchName.Create(name[GitRefs.Heads.Length..]).Match(Option<BranchName>.Some, _ => Option<BranchName>.None)
                    : Option<BranchName>.None,
                _ => Option<BranchName>.None);

    public Task<Result<Option<string>, WorkspaceFailure>> CommittedBlobAsync(
        string repository,
        CommitSha commit,
        string path,
        CancellationToken cancellationToken) =>
        RunAsync([.. Pristine, "-C", repository, "ls-tree", "--full-tree", commit.Value, "--", path], WorkspaceFailure.GitFailed, cancellationToken)
            .MapAsync(output => output.Split('\t', 2)[0].Split(' ') is [_, "blob", var blob] ? Option<string>.Some(blob) : Option<string>.None);

    public Task<Result<string, WorkspaceFailure>> BlobContentAsync(string repository, string blob, CancellationToken cancellationToken) =>
        RunAsync([.. Pristine, "-C", repository, "cat-file", "blob", blob], WorkspaceFailure.GitFailed, cancellationToken);

    public async Task<Result<Option<string>, WorkspaceFailure>> WorktreeBlobAsync(
        WorkspaceLocation location,
        string path,
        CancellationToken cancellationToken) =>
        File.Exists(Path.Combine(location.Path, path))
            ? await RunAsync([.. Pristine, "-C", location.Path, "hash-object", "--", path], WorkspaceFailure.GitFailed, location.Path, cancellationToken)
                .MapAsync(output => Option<string>.Some(output.Trim()))
            : Option<string>.None;

    public async Task<Result<bool, WorkspaceFailure>> BranchExistsAsync(
        string repository,
        BranchName branch,
        CancellationToken cancellationToken) =>
        (await processes.RunAsync(
            new ProcessRequest("git", ["-C", repository, "rev-parse", "--verify", "--quiet", $"{GitRefs.Heads}{branch.Value}"]),
            cancellationToken))
            .MapError(_ => WorkspaceFailure.GitFailed)
            .Map(outcome => outcome.Succeeded);

    public Task<Result<WorkspaceLocation, WorkspaceFailure>> AddWorktreeAsync(
        WorkspaceLocation location,
        BranchName branch,
        CommitSha commit,
        CancellationToken cancellationToken) =>
        RunAsync(["-C", location.Repository, "worktree", "add", "-b", branch.Value, location.Path, commit.Value], WorkspaceFailure.GitFailed, cancellationToken)
            .MapAsync(_ => location);

    public Task<Result<CommitSha, WorkspaceFailure>> CommitAllAsync(
        WorkspaceLocation location,
        string label,
        CancellationToken cancellationToken) =>
        RunAsync(["-C", location.Path, "add", "--all"], WorkspaceFailure.GitFailed, location.Path, cancellationToken)
            .BindAsync(_ => RunAsync(
                [.. CheckpointIdentity, "-C", location.Path, "commit", "--allow-empty", "--no-verify", "--quiet", "--message", label],
                WorkspaceFailure.GitFailed,
                location.Path,
                cancellationToken))
            .BindAsync(_ => RunAsync(["-C", location.Path, "rev-parse", "HEAD"], WorkspaceFailure.GitFailed, location.Path, cancellationToken))
            .BindAsync(output => CommitSha.Create(output).MapError(_ => WorkspaceFailure.GitFailed));

    public Task<Result<WorkspaceLocation, WorkspaceFailure>> RemoveWorktreeAsync(
        WorkspaceLocation location,
        BranchName branch,
        CancellationToken cancellationToken) =>
        RunAsync(["-C", location.Repository, "worktree", "remove", "--force", location.Path], WorkspaceFailure.GitFailed, cancellationToken)
            .BindAsync(_ => RunAsync(["-C", location.Repository, "branch", "-D", branch.Value], WorkspaceFailure.GitFailed, cancellationToken))
            .MapAsync(_ => location);

    public Task<Result<string, WorkspaceFailure>> PruneWorktreesAsync(string repository, CancellationToken cancellationToken) =>
        RunAsync(["-C", repository, "worktree", "prune"], WorkspaceFailure.GitFailed, cancellationToken);

    private Task<Result<string, WorkspaceFailure>> RunAsync(
        IReadOnlyList<string> arguments,
        WorkspaceFailure failure,
        CancellationToken cancellationToken) =>
        RunAsync(arguments, failure, Option<string>.None, cancellationToken);

    private async Task<Result<string, WorkspaceFailure>> RunAsync(
        IReadOnlyList<string> arguments,
        WorkspaceFailure failure,
        Option<string> worktree,
        CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest("git", arguments, worktree), cancellationToken))
            .MapError(_ => failure)
            .Bind<string>(outcome => outcome.Succeeded ? outcome.Output : failure);
}
