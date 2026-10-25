using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Workspaces;
using Microsoft.Extensions.Logging;

namespace Avala.Workspaces.Git;

internal sealed partial class GitCli(IProcessRunner processes, ILogger<GitCli> logger) : IGit, IAsyncDisposable
{
    private static readonly string[] CheckpointIdentity =
        ["-c", "user.name=Avala", "-c", "user.email=avala@localhost", "-c", "commit.gpgsign=false"];

    private static readonly string[] Pristine = GitRefs.Pristine;

    private readonly SerialExecutor administration = new();

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
        administration.RunAsync(
            token => AdministrationAsync(["-C", location.Repository, "worktree", "add", "-b", branch.Value, location.Path, commit.Value], token)
                .MapAsync(_ => location),
            cancellationToken);

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

    public Task<Result<string, WorkspaceFailure>> ResetTreeAsync(WorkspaceLocation location, CommitSha commit, CancellationToken cancellationToken) =>
        RunAsync(["-C", location.Path, "read-tree", "--reset", "-u", commit.Value], WorkspaceFailure.GitFailed, location.Path, cancellationToken);

    public Task<Result<bool, WorkspaceFailure>> UnchangedSinceAsync(
        WorkspaceLocation location,
        CommitSha commit,
        CancellationToken cancellationToken) =>
        RunAsync(["-C", location.Path, "rev-parse", "HEAD"], WorkspaceFailure.GitFailed, location.Path, cancellationToken)
            .BindAsync(async head => head.Trim() == commit.Value
                ? await RunAsync(["-C", location.Path, "status", "--porcelain"], WorkspaceFailure.GitFailed, location.Path, cancellationToken)
                    .MapAsync(status => status.Trim().Length == 0)
                : Result<bool, WorkspaceFailure>.Success(false));

    public Task<Result<WorkspaceLocation, WorkspaceFailure>> RemoveWorktreeAsync(
        WorkspaceLocation location,
        BranchName branch,
        WorkspaceId workspace,
        CancellationToken cancellationToken) =>
        administration.RunAsync(
            token => AdministrationAsync(["-C", location.Repository, "worktree", "remove", "--force", location.Path], token)
                .BindAsync(_ => AdministrationAsync(["-C", location.Repository, "branch", "-D", branch.Value], token))
                .BindAsync(_ => AdministrationAsync(["-C", location.Repository, "update-ref", "-d", GitRefs.MergedOf(workspace)], token))
                .MapAsync(_ => location),
            cancellationToken);

    public Task<Result<bool, WorkspaceFailure>> HasChangesAsync(WorkspaceLocation location, CancellationToken cancellationToken) =>
        RunAsync(["-C", location.Path, "status", "--porcelain"], WorkspaceFailure.GitFailed, location.Path, cancellationToken)
            .MapAsync(status => status.Trim().Length > 0);

    public async Task<Result<bool, WorkspaceFailure>> OnBranchAsync(WorkspaceLocation location, BranchName branch, CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest("git", ["-C", location.Path, "symbolic-ref", "-q", "HEAD"], location.Path), cancellationToken))
            .MapError(_ => WorkspaceFailure.GitFailed)
            .Map(outcome => outcome.Succeeded && outcome.Output.Trim() == $"{GitRefs.Heads}{branch.Value}");

    public Task<Result<IReadOnlyList<string>, WorkspaceFailure>> IgnoredAsync(WorkspaceLocation location, CancellationToken cancellationToken) =>
        RunAsync(["-C", location.Path, "ls-files", "--others", "--ignored", "--exclude-standard", "--directory", "-z"], WorkspaceFailure.GitFailed, location.Path, cancellationToken)
            .MapAsync(output => (IReadOnlyList<string>)[.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries)]);

    public Task<Result<WorkspaceLocation, WorkspaceFailure>> KeepTipAsync(
        WorkspaceLocation location,
        BranchName branch,
        WorkspaceId workspace,
        CancellationToken cancellationToken) =>
        administration.RunAsync(
            token => AdministrationAsync(["-C", location.Repository, "update-ref", "-m", "avala: shelved", GitRefs.ShelvedOf(workspace), $"{GitRefs.Heads}{branch.Value}"], token)
                .MapAsync(_ => location),
            cancellationToken);

    public Task<Result<WorkspaceLocation, WorkspaceFailure>> ReleaseWorktreeAsync(
        WorkspaceLocation location,
        BranchName branch,
        CancellationToken cancellationToken) =>
        administration.RunAsync(
            async token =>
            {
                if (Directory.Exists(location.Path) && (await AdministrationAsync(["-C", location.Repository, "worktree", "remove", "--force", location.Path], token)).IsFailure)
                {
                    DeleteLeftover(location.Path);
                }

                _ = await AdministrationAsync(["-C", location.Repository, "worktree", "prune"], token);

                if (Directory.Exists(location.Path))
                {
                    return Result<WorkspaceLocation, WorkspaceFailure>.Failure(WorkspaceFailure.GitFailed);
                }

                if (await BranchPresentAsync(location.Repository, branch, token))
                {
                    _ = await AdministrationAsync(["-C", location.Repository, "branch", "-D", branch.Value], token);
                }

                return Result<WorkspaceLocation, WorkspaceFailure>.Success(location);
            },
            cancellationToken);

    public Task<Result<WorkspaceLocation, WorkspaceFailure>> RebuildWorktreeAsync(
        WorkspaceLocation location,
        BranchName branch,
        WorkspaceId workspace,
        CancellationToken cancellationToken) =>
        administration.RunAsync(
            async token =>
            {
                if (!await CheckedOutAsync(location, branch, token))
                {
                    _ = await AdministrationAsync(["-C", location.Repository, "worktree", "prune"], token);
                    DeleteLeftover(location.Path);

                    var added = await BranchPresentAsync(location.Repository, branch, token)
                        ? await AdministrationAsync(["-C", location.Repository, "update-ref", $"{GitRefs.Heads}{branch.Value}", GitRefs.ShelvedOf(workspace)], token)
                            .BindAsync(_ => AdministrationAsync(["-C", location.Repository, "worktree", "add", location.Path, branch.Value], token))
                        : await AdministrationAsync(["-C", location.Repository, "worktree", "add", "-b", branch.Value, location.Path, GitRefs.ShelvedOf(workspace)], token);

                    if (added.IsFailure)
                    {
                        return Result<WorkspaceLocation, WorkspaceFailure>.Failure(WorkspaceFailure.GitFailed);
                    }
                }

                _ = await AdministrationAsync(["-C", location.Repository, "update-ref", "-d", GitRefs.ShelvedOf(workspace)], token);

                return Result<WorkspaceLocation, WorkspaceFailure>.Success(location);
            },
            cancellationToken);

    public Task<Result<WorkspaceLocation, WorkspaceFailure>> RemoveShelvedAsync(
        WorkspaceLocation location,
        WorkspaceId workspace,
        CancellationToken cancellationToken) =>
        administration.RunAsync(
            token => AdministrationAsync(["-C", location.Repository, "update-ref", "-d", GitRefs.ShelvedOf(workspace)], token)
                .BindAsync(_ => AdministrationAsync(["-C", location.Repository, "update-ref", "-d", GitRefs.MergedOf(workspace)], token))
                .MapAsync(_ => location),
            cancellationToken);

    public Task<Result<CommitSha, WorkspaceFailure>> ShelvedTipAsync(string repository, WorkspaceId workspace, CancellationToken cancellationToken) =>
        ResolveCommitAsync(repository, GitRefs.ShelvedOf(workspace), cancellationToken);

    public async Task<bool> ContainsAsync(string repository, string reference, CommitSha commit, CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest("git", ["-C", repository, "merge-base", "--is-ancestor", commit.Value, reference]), cancellationToken))
            .Match(outcome => outcome.Succeeded, _ => false);

    public Task<Result<string, WorkspaceFailure>> PruneWorktreesAsync(string repository, CancellationToken cancellationToken) =>
        administration.RunAsync(token => AdministrationAsync(["-C", repository, "worktree", "prune"], token), cancellationToken);

    public ValueTask DisposeAsync() => administration.DisposeAsync();

    private async Task<bool> BranchPresentAsync(string repository, BranchName branch, CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest("git", ["-C", repository, "rev-parse", "--verify", "--quiet", $"{GitRefs.Heads}{branch.Value}"]), cancellationToken))
            .Match(outcome => outcome.Succeeded, _ => false);

    private async Task<bool> CheckedOutAsync(WorkspaceLocation location, BranchName branch, CancellationToken cancellationToken) =>
        Directory.Exists(location.Path)
        && (await processes.RunAsync(new ProcessRequest("git", ["-C", location.Path, "rev-parse", "--show-toplevel"], location.Path), cancellationToken))
            .Match(outcome => outcome.Succeeded && location.IsAt(outcome.Output.Trim()), _ => false)
        && (await OnBranchAsync(location, branch, cancellationToken)).Match(on => on, _ => false);

    private static void DeleteLeftover(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                {
                    file.Attributes = FileAttributes.Normal;
                }

                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }
    }

    private async Task<Result<string, WorkspaceFailure>> AdministrationAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest("git", arguments), cancellationToken))
            .MapError(_ => WorkspaceFailure.GitFailed)
            .Bind(Administered(arguments));

    private Func<ProcessOutcome, Result<string, WorkspaceFailure>> Administered(IReadOnlyList<string> arguments) =>
        outcome =>
        {
            if (outcome.Succeeded)
            {
                return outcome.Output;
            }

            LogAdministrationFailed(string.Join(' ', arguments), outcome.ExitCode, outcome.Error.Trim());

            return WorkspaceFailure.GitFailed;
        };

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "git {Arguments} exited with {ExitCode}: {Error}")]
    private partial void LogAdministrationFailed(string arguments, int exitCode, string error);
}
