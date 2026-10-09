using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.BaseFiles;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Git;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Tests.Provisioning;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces.Tests.Git;

public sealed class GitWorkspacesTests
{
    private static readonly IProcessRunner Processes =
        new ServiceCollection().AddRuntime(new AvalaPaths(Path.GetTempPath())).BuildServiceProvider().GetRequiredService<IProcessRunner>();

    private const string Rules = ".avala/checks.json";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APreparedWorkspaceIsAWorktreeOnItsOwnBranchAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);

        var workspace = Outcomes.Succeeds(await Service(repository).PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));

        Assert.True(File.Exists(Path.Combine(workspace.Path, "README.md")));
        Assert.Equal(workspace.Branch, await repository.GitInAsync(workspace.Path, Cancellation, "branch", "--show-current"));
        Assert.Equal(await repository.GitAsync(Cancellation, "rev-parse", "HEAD"), workspace.BaseCommit);
    }

    [Fact]
    public async Task ABaseFileKeepsItsCommittedContentWhateverTheWorktreeOrTheRepositoryDoLaterAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await repository.CommitAsync(Rules, "committed", Cancellation);
        var store = new InMemoryWorkspaceStore();
        var service = Service(repository, store);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, Rules), "edited by the agent", Cancellation);
        Outcomes.Succeeds(await service.CheckpointAsync(workspace.Id, "turn 1", Cancellation));
        await repository.CommitAsync(Rules, "committed later", Cancellation);

        var file = Outcomes.Succeeds(await BaseFiles(store).ReadAsync(workspace.Path, Rules, Cancellation));

        Assert.Equal(new BaseFile(Rules, new FileOrigin(workspace.BaseCommit, EditedInWorktree: true), "committed"), file);
    }

    [Fact]
    public async Task ABaseFileTheWorktreeLeftAloneIsNotReportedAsEditedAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await repository.CommitAsync(Rules, "committed", Cancellation);
        var store = new InMemoryWorkspaceStore();
        var workspace = Outcomes.Succeeds(await Service(repository, store).PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));

        var file = Outcomes.Succeeds(await BaseFiles(store).ReadAsync(workspace.Path, Rules, Cancellation));

        Assert.Equal(new BaseFile(Rules, new FileOrigin(workspace.BaseCommit, EditedInWorktree: false), "committed"), file);
    }

    [Fact]
    public async Task AFileAbsentFromTheBaseHasNoContentEvenOnceTheWorktreeAddsItAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var store = new InMemoryWorkspaceStore();
        var workspace = Outcomes.Succeeds(await Service(repository, store).PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".avala"));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, Rules), "added by the agent", Cancellation);

        var file = Outcomes.Succeeds(await BaseFiles(store).ReadAsync(workspace.Path, Rules, Cancellation));

        Assert.Equal(new BaseFile(Rules, new FileOrigin(workspace.BaseCommit, EditedInWorktree: true), Option<string>.None), file);
    }

    [Fact]
    public async Task AFolderThatIsNoWorkspaceHasNoBaseAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);

        var failure = Outcomes.FailsWith(await BaseFiles(new InMemoryWorkspaceStore()).ReadAsync(repository.Path, Rules, Cancellation));

        Assert.Equal(WorkspaceFailure.UnknownWorkspace, failure);
    }

    [Fact]
    public async Task ACurrentFileIsReadFromTheCommitTheRepositorysHeadPointsAtEachTimeWithItsUncommittedEditReportedAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await repository.CommitAsync(Rules, "committed", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(repository.Path, Rules), "edited in the checkout", Cancellation);
        var reader = BaseFiles(new InMemoryWorkspaceStore());

        var first = Outcomes.Succeeds(await reader.ReadCurrentAsync(Path.Combine(repository.Path, ".avala"), Rules, Cancellation));
        await repository.CommitAsync(Rules, "committed later", Cancellation);
        var second = Outcomes.Succeeds(await reader.ReadCurrentAsync(repository.Path, Rules, Cancellation));

        Assert.Equal(new BaseFile(Rules, new FileOrigin(await repository.GitAsync(Cancellation, "rev-parse", "HEAD~1"), EditedInWorktree: true), "committed"), first);
        Assert.Equal(new BaseFile(Rules, new FileOrigin(await repository.GitAsync(Cancellation, "rev-parse", "HEAD"), EditedInWorktree: false), "committed later"), second);
    }

    [Fact]
    public async Task AFolderOutsideAnyRepositoryHasNoCurrentFileAsync()
    {
        var outside = Directory.CreateTempSubdirectory("avala-outside-");

        try
        {
            var failure = Outcomes.FailsWith(await BaseFiles(new InMemoryWorkspaceStore()).ReadCurrentAsync(outside.FullName, Rules, Cancellation));

            Assert.Equal(WorkspaceFailure.NotAGitRepository, failure);
        }
        finally
        {
            outside.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ACheckpointCommitsEveryChangeInTheWorktreeAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var service = Service(repository);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "login.cs"), "class Login;\n", Cancellation);

        var checkpoint = Outcomes.Succeeds(await service.CheckpointAsync(workspace.Id, "turn 1", Cancellation));

        Assert.Equal(checkpoint.Commit, await repository.GitInAsync(workspace.Path, Cancellation, "rev-parse", "HEAD"));
        Assert.Equal("login.cs", await repository.GitInAsync(workspace.Path, Cancellation, "show", "--name-only", "--format=", "HEAD"));
        Assert.Empty(await repository.GitInAsync(workspace.Path, Cancellation, "status", "--porcelain"));
    }

    [Fact]
    public async Task RemovingDeletesTheWorktreeAndItsBranchAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var service = Service(repository);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));

        Outcomes.Succeeds(await service.RemoveAsync(workspace.Id, Cancellation));

        Assert.False(Directory.Exists(workspace.Path));
        Assert.Empty(await repository.GitAsync(Cancellation, "branch", "--list", workspace.Branch));
    }

    [Fact]
    public async Task ReconcilingReportsStrayFoldersAndMissingWorktreesWithoutTouchingThemAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var service = Service(repository);
        var kept = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        var lost = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        Directory.Delete(lost.Path, recursive: true);
        var stray = Directory.CreateDirectory(Path.Combine(repository.WorktreeRoot, "left-behind")).FullName;

        var found = await service.ReconcileAsync(Cancellation);

        Assert.Equal([stray], found.Strays);
        Assert.Equal([lost], found.Missing);
        Assert.True(Directory.Exists(stray));
        Assert.Equal(lost, Outcomes.Succeeds(await service.FindAsync(lost.Id, Cancellation)));
        Assert.Equal(kept, Outcomes.Succeeds(await service.FindAtAsync(kept.Path, Cancellation)));
    }

    [Fact]
    public async Task CleaningDeletesTheStrayFoldersAndForgetsTheMissingWorktreesButKeepsTheirBranchesAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var service = Service(repository);
        var lost = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        Directory.Delete(lost.Path, recursive: true);
        var stray = Directory.CreateDirectory(Path.Combine(repository.WorktreeRoot, "left-behind")).FullName;
        await File.WriteAllTextAsync(Path.Combine(stray, "output.log"), "built", Cancellation);

        var cleaned = await service.CleanAsync(await service.ReconcileAsync(Cancellation), Cancellation);

        Assert.Equal([stray], cleaned.Strays);
        Assert.Equal([lost], cleaned.Missing);
        Assert.False(Directory.Exists(stray));
        Assert.Equal(WorkspaceFailure.UnknownWorkspace, Outcomes.FailsWith(await service.FindAsync(lost.Id, Cancellation)));
        Assert.DoesNotContain(lost.Path, await repository.GitAsync(Cancellation, "worktree", "list"), StringComparison.Ordinal);
        Assert.NotEmpty(await repository.GitAsync(Cancellation, "branch", "--list", lost.Branch));
        var after = await service.ReconcileAsync(Cancellation);
        Assert.Empty(after.Strays);
        Assert.Empty(after.Missing);
    }

    [Fact]
    public async Task CheckpointingRunsGitInTheWorktreeSoItsProcessesJoinTheWorktreesTreeAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var recording = new RecordingRunner(Processes);
        var store = new InMemoryWorkspaceStore();
        var settings = new WorkspaceSettings(repository.WorktreeRoot);
        var git = new GitCli(recording);
        var service = new WorkspaceService(git, store, settings, new WorktreeReconciler(git, store, settings));
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));

        Outcomes.Succeeds(await service.CheckpointAsync(workspace.Id, "turn 1", Cancellation));

        Assert.Equal(
            ["add", "commit", "rev-parse"],
            recording.Requests.Where(request => request.WorkingDirectory == Option<string>.Some(workspace.Path)).Select(request => request.Arguments.SkipWhile(argument => argument != workspace.Path).ElementAt(1)));
    }

    private sealed class RecordingRunner(IProcessRunner inner) : IProcessRunner
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<ProcessRequest> requests = new();

        public IReadOnlyList<ProcessRequest> Requests => [.. requests];

        public ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            requests.Enqueue(request);

            return inner.RunAsync(request, cancellationToken);
        }
    }

    [Fact]
    public async Task AFolderOutsideAnyRepositoryIsRefusedAsync()
    {
        var outside = Directory.CreateTempSubdirectory("avala-outside-");

        try
        {
            var failure = Outcomes.FailsWith(await Service(outside.FullName).PrepareAsync(new WorkspaceRequest(outside.FullName), Cancellation));

            Assert.Equal(WorkspaceFailure.NotAGitRepository, failure);
        }
        finally
        {
            outside.Delete(recursive: true);
        }
    }

    private static WorkspaceService Service(TemporaryRepository repository, InMemoryWorkspaceStore? store = null) =>
        Service(repository.WorktreeRoot, store);

    private static WorkspaceService Service(string worktreeRoot, InMemoryWorkspaceStore? store = null)
    {
        var git = new GitCli(Processes);
        var kept = store ?? new InMemoryWorkspaceStore();
        var settings = new WorkspaceSettings(worktreeRoot);

        return new(git, kept, settings, new WorktreeReconciler(git, kept, settings));
    }

    private static BaseFileReader BaseFiles(InMemoryWorkspaceStore store) => new(new GitCli(Processes), store);
}
