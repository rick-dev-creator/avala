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

    private static WorkspaceService Service(string worktreeRoot, InMemoryWorkspaceStore? store = null) =>
        new(new GitCli(Processes), store ?? new InMemoryWorkspaceStore(), new WorkspaceSettings(worktreeRoot));

    private static BaseFileReader BaseFiles(InMemoryWorkspaceStore store) => new(new GitCli(Processes), store);
}
