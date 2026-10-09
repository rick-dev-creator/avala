using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Workspaces.Changes;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Git;

internal sealed class GitChangesCli(IProcessRunner processes) : IGitChanges
{
    private const string Avala = "Avala";


    public async Task<Result<Option<CommitSha>, WorkspaceFailure>> TipOfAsync(string repository, BranchName branch, CancellationToken cancellationToken) =>
        (await RunAsync(["-C", repository, "rev-parse", "--verify", "--quiet", $"{GitRefs.Heads}{branch.Value}^{{commit}}"], cancellationToken))
            .Bind<Option<CommitSha>>(outcome => outcome.ExitCode switch
            {
                0 => CommitSha.Create(outcome.Output).Match(Option<CommitSha>.Some, _ => Option<CommitSha>.None),
                1 => Option<CommitSha>.None,
                _ => WorkspaceFailure.GitFailed,
            });

    public Task<Result<string, WorkspaceFailure>> TreeOfAsync(string repository, CommitSha commit, CancellationToken cancellationToken) =>
        OutputAsync(["-C", repository, "rev-parse", "--verify", $"{commit.Value}^{{tree}}"], cancellationToken).MapAsync(output => output.Trim());

    public async Task<Result<IReadOnlyList<FileChange>, WorkspaceFailure>> ChangedFilesAsync(
        string repository,
        CommitSha from,
        CommitSha to,
        CancellationToken cancellationToken)
    {
        if (!(await OutputAsync([.. Diff(repository), "--name-status", "-z", from.Value, to.Value], cancellationToken)).TryGetValue(out var statuses, out var failure)
            || !(await OutputAsync([.. Diff(repository), "--numstat", "-z", from.Value, to.Value], cancellationToken)).TryGetValue(out var counts, out failure))
        {
            return failure;
        }

        return Result<IReadOnlyList<FileChange>, WorkspaceFailure>.Success(GitDiffs.Changes(statuses, counts));
    }

    public async Task<Result<Option<FileDiff>, WorkspaceFailure>> FileDiffAsync(
        string repository,
        CommitSha from,
        CommitSha to,
        string path,
        CancellationToken cancellationToken) =>
        await OutputAsync([.. Diff(repository), "--unified=3", from.Value, to.Value, "--", path], cancellationToken)
            .MapAsync(output => GitDiffs.OfFile(path, output));

    public async Task<Result<MergedTree, WorkspaceFailure>> MergeTreeAsync(
        string repository,
        CommitSha mergeBase,
        CommitSha target,
        CommitSha work,
        CancellationToken cancellationToken) =>
        (await RunAsync(
            [.. GitRefs.Pristine, "-C", repository, "merge-tree", "--write-tree", "--name-only", "--no-messages", "-z", $"--merge-base={mergeBase.Value}", target.Value, work.Value],
            cancellationToken))
            .Bind<MergedTree>(outcome => outcome.ExitCode is 0 or 1 && outcome.Output.Split(['\0', '\n'], StringSplitOptions.RemoveEmptyEntries) is [var tree, .. var conflicts]
                ? new MergedTree(tree, outcome.ExitCode == 0 ? [] : conflicts)
                : WorkspaceFailure.GitFailed);

    public async Task<Result<Option<string>, WorkspaceFailure>> CheckoutOfAsync(string repository, BranchName branch, CancellationToken cancellationToken) =>
        await OutputAsync(["-C", repository, "worktree", "list", "--porcelain", "-z"], cancellationToken)
            .MapAsync(output => GitDiffs.CheckoutOf(output, $"branch {GitRefs.Heads}{branch.Value}"));

    public async Task<Result<bool, WorkspaceFailure>> IsCleanAsync(string checkout, CancellationToken cancellationToken) =>
        await OutputAsync(["-C", checkout, "status", "--porcelain=v1", "-z", "--untracked-files=no"], cancellationToken)
            .MapAsync(output => output.Length == 0);

    public async Task<Result<CommitSha, WorkspaceFailure>> CommitTreeAsync(
        string repository,
        string tree,
        CommitSha parent,
        string message,
        CancellationToken cancellationToken) =>
        await OutputAsync(
            [.. await IdentityAsync(repository, cancellationToken), "-C", repository, "commit-tree", "--no-gpg-sign", tree, "-p", parent.Value, "-m", message],
            cancellationToken)
            .BindAsync(output => CommitSha.Create(output).MapError(_ => WorkspaceFailure.GitFailed));

    public async Task<bool> MoveBranchAsync(string repository, BranchName branch, CommitSha to, CommitSha from, CancellationToken cancellationToken) =>
        (await RunAsync(["-C", repository, "update-ref", "-m", "avala: approve", $"{GitRefs.Heads}{branch.Value}", to.Value, from.Value], cancellationToken))
            .Match(outcome => outcome.Succeeded, _ => false);

    public async Task<bool> AdvanceCheckoutAsync(string checkout, CommitSha from, CommitSha to, CancellationToken cancellationToken)
    {
        _ = await RunAsync(["-C", checkout, "update-index", "-q", "--refresh"], cancellationToken);

        return (await RunAsync(["-C", checkout, "read-tree", "-m", "-u", from.Value, to.Value], cancellationToken)).Match(outcome => outcome.Succeeded, _ => false);
    }

    private static string[] Diff(string repository) =>
        [.. GitRefs.Pristine, "-C", repository, "diff", "--no-renames", "--no-ext-diff", "--no-textconv", "--no-color", "--no-relative"];

    private async Task<string[]> IdentityAsync(string repository, CancellationToken cancellationToken)
    {
        var name = await OutputAsync(["-C", repository, "config", "--get", "user.name"], cancellationToken);
        var email = await OutputAsync(["-C", repository, "config", "--get", "user.email"], cancellationToken);

        return name.IsSuccess && email.IsSuccess ? [] : ["-c", $"user.name={Avala}", "-c", "user.email=avala@localhost"];
    }

    private async Task<Result<string, WorkspaceFailure>> OutputAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        (await RunAsync(arguments, cancellationToken)).Bind<string>(outcome => outcome.Succeeded ? outcome.Output : WorkspaceFailure.GitFailed);

    private async Task<Result<ProcessOutcome, WorkspaceFailure>> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest("git", arguments), cancellationToken)).MapError(_ => WorkspaceFailure.GitFailed);
}
