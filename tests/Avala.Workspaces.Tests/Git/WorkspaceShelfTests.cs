using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Git;
using Avala.Workspaces.Provisioning;
using Avala.Workspaces.Tests.Provisioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Workspaces.Tests.Git;

public sealed class WorkspaceShelfTests
{
    private static readonly IProcessRunner Processes =
        new ServiceCollection().AddRuntime(new AvalaPaths(Path.GetTempPath())).BuildServiceProvider().GetRequiredService<IProcessRunner>();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ShelvingRemovesTheFolderAndTheBranchButKeepsEveryChangeUnderAHiddenRefAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var (service, shelf) = Services(repository);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "login.cs"), "class Login;\n", Cancellation);

        var shelved = Outcomes.Succeeds(await shelf.ShelveAsync(workspace.Id, Cancellation));

        Assert.True(shelved.Shelved);
        Assert.False(Directory.Exists(workspace.Path));
        Assert.Empty(await repository.GitAsync(Cancellation, "branch", "--list", workspace.Branch));
        Assert.Equal("class Login;", await repository.GitAsync(Cancellation, "show", $"refs/avala/shelved/{workspace.Id.Value:N}:login.cs"));
        Assert.True(Outcomes.Succeeds(await service.FindAsync(workspace.Id, Cancellation)).Shelved);
    }

    [Fact]
    public async Task RebuildingRecreatesTheWorktreeAtItsPathOnItsBranchAndDropsTheHiddenRefAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var (service, shelf) = Services(repository);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "login.cs"), "class Login;\n", Cancellation);
        Outcomes.Succeeds(await shelf.ShelveAsync(workspace.Id, Cancellation));

        var rebuilt = Outcomes.Succeeds(await shelf.RebuildAsync(workspace.Id, Cancellation));

        Assert.Equal(workspace with { }, rebuilt);
        Assert.Equal("class Login;\n", await File.ReadAllTextAsync(Path.Combine(workspace.Path, "login.cs"), Cancellation));
        Assert.Equal(workspace.Branch, await repository.GitInAsync(workspace.Path, Cancellation, "branch", "--show-current"));
        Assert.Empty(await repository.GitAsync(Cancellation, "for-each-ref", "refs/avala/shelved/"));
    }

    [Fact]
    public async Task AWorktreeWhoseHeadLeftItsBranchIsNotShelvedAndKeepsEverythingAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var (service, shelf) = Services(repository);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        _ = await repository.GitInAsync(workspace.Path, Cancellation, "checkout", "--detach");
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "login.cs"), "class Login;\n", Cancellation);

        Assert.Equal(WorkspaceFailure.InvalidState, Outcomes.FailsWith(await shelf.ShelveAsync(workspace.Id, Cancellation)));
        Assert.Equal("class Login;\n", await File.ReadAllTextAsync(Path.Combine(workspace.Path, "login.cs"), Cancellation));
        Assert.False(Outcomes.Succeeds(await service.FindAsync(workspace.Id, Cancellation)).Shelved);
    }

    [Fact]
    public async Task RebuildingAWorktreeAlreadyAddedBackOnlyMarksItRebuiltAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var (service, shelf) = Services(repository);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        Outcomes.Succeeds(await shelf.ShelveAsync(workspace.Id, Cancellation));
        _ = await repository.GitAsync(Cancellation, "worktree", "add", "-b", workspace.Branch, workspace.Path, $"refs/avala/shelved/{workspace.Id.Value:N}");

        var rebuilt = Outcomes.Succeeds(await shelf.RebuildAsync(workspace.Id, Cancellation));

        Assert.False(rebuilt.Shelved);
        Assert.Equal(workspace.Branch, await repository.GitInAsync(workspace.Path, Cancellation, "branch", "--show-current"));
    }

    [Fact]
    public async Task RebuildingWhenTheBranchSurvivedTheShelvingChecksItOutAgainAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var (service, shelf) = Services(repository);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "login.cs"), "class Login;\n", Cancellation);
        Outcomes.Succeeds(await shelf.ShelveAsync(workspace.Id, Cancellation));
        _ = await repository.GitAsync(Cancellation, "branch", workspace.Branch, "HEAD");

        Outcomes.Succeeds(await shelf.RebuildAsync(workspace.Id, Cancellation));

        Assert.Equal("class Login;\n", await File.ReadAllTextAsync(Path.Combine(workspace.Path, "login.cs"), Cancellation));
    }

    [Fact]
    public async Task AWorktreeWhoseFolderWasDeletedIsStillShelvedAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var (service, shelf) = Services(repository);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        Directory.Delete(workspace.Path, recursive: true);

        Assert.True(Outcomes.Succeeds(await shelf.ShelveAsync(workspace.Id, Cancellation)).Shelved);
        Assert.True(Directory.Exists(Outcomes.Succeeds(await shelf.RebuildAsync(workspace.Id, Cancellation)).Path));
    }

    [Fact]
    public async Task WorkIsDeliveredOnceItsBaseBranchHoldsItAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var (service, shelf) = Services(repository);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "login.cs"), "class Login;\n", Cancellation);
        Outcomes.Succeeds(await service.CheckpointAsync(workspace.Id, "Attempt 1", Cancellation));

        var before = Outcomes.Succeeds(await shelf.DeliveredAsync(workspace.Id, Cancellation));
        _ = await repository.GitAsync(Cancellation, "merge", "--ff-only", workspace.Branch);

        Assert.False(before);
        Assert.True(Outcomes.Succeeds(await shelf.DeliveredAsync(workspace.Id, Cancellation)));
    }

    [Fact]
    public async Task WorkIsDeliveredOnceTheUpstreamOfItsBaseBranchHoldsItAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var (service, shelf) = Services(repository);
        var branch = await repository.GitAsync(Cancellation, "branch", "--show-current");
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "login.cs"), "class Login;\n", Cancellation);
        var work = Outcomes.Succeeds(await service.CheckpointAsync(workspace.Id, "Attempt 1", Cancellation)).Commit;

        _ = await repository.GitAsync(Cancellation, "update-ref", "refs/remotes/origin/merged", work);
        _ = await repository.GitAsync(Cancellation, "config", $"branch.{branch}.remote", "origin");
        _ = await repository.GitAsync(Cancellation, "config", $"branch.{branch}.merge", "refs/heads/merged");
        _ = await repository.GitAsync(Cancellation, "config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/*");

        Assert.True(Outcomes.Succeeds(await shelf.DeliveredAsync(workspace.Id, Cancellation)));
    }

    [Fact]
    public async Task RemovingAShelvedWorkspaceDropsItsHiddenRefAndForgetsItAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        var (service, shelf) = Services(repository);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest(repository.Path), Cancellation));
        Outcomes.Succeeds(await shelf.ShelveAsync(workspace.Id, Cancellation));

        Outcomes.Succeeds(await service.RemoveAsync(workspace.Id, Cancellation));

        Assert.Empty(await repository.GitAsync(Cancellation, "for-each-ref", "refs/avala/"));
        Assert.Equal(WorkspaceFailure.UnknownWorkspace, Outcomes.FailsWith(await service.FindAsync(workspace.Id, Cancellation)));
    }

    private static (WorkspaceService Service, WorkspaceShelf Shelf) Services(TemporaryRepository repository)
    {
        var git = new GitCli(Processes, NullLogger<GitCli>.Instance);
        var store = new InMemoryWorkspaceStore();
        var settings = new WorkspaceSettings(repository.WorktreeRoot);

        return (new WorkspaceService(git, store, settings, new WorktreeReconciler(git, store, settings, TimeProvider.System)), new WorkspaceShelf(git, new GitChangesCli(Processes), store, settings));
    }
}
