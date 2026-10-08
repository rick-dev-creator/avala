using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Application;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Infrastructure;
using Avala.Workspaces.Tests.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces.Tests.Infrastructure;

public sealed class GitWorkspacesTests
{
    private static readonly IProcessRunner Processes =
        new ServiceCollection().AddRuntime(new AvalaPaths(Path.GetTempPath())).BuildServiceProvider().GetRequiredService<IProcessRunner>();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APreparedWorkspaceIsAWorktreeOnItsOwnBranchAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);

        var workspace = Outcomes.Succeeds(await Service(repository).PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));

        Assert.True(File.Exists(Path.Combine(workspace.Path, "README.md")));
        Assert.Equal(workspace.Branch, await repository.GitInAsync(workspace.Path, Cancellation, "branch", "--show-current"));
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

    private static WorkspaceService Service(TemporaryRepository repository) => Service(repository.WorktreeRoot);

    private static WorkspaceService Service(string worktreeRoot) =>
        new(new GitCli(Processes), new InMemoryWorkspaceStore(), new WorkspaceSettings(worktreeRoot));
}
