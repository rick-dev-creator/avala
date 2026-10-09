using Avala.Testing;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Provisioning;

namespace Avala.Workspaces.Tests.Provisioning;

public sealed class WorkspaceServiceTests
{
    private const string Root = "/home/dev/.local/share/Avala/worktrees";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PreparesAWorktreeOnAFreshBranchUnderTheRootAsync()
    {
        var git = new FakeGit();

        var info = Outcomes.Succeeds(await Service(git).PrepareAsync(new WorkspaceRequest("/repos/shop/src"), Cancellation));

        Assert.Equal(Path.Combine(Root, $"{info.Id.Value:N}"), info.Path);
        Assert.Equal($"avala/{info.Id.Value:N}", info.Branch);
        Assert.Equal([info.Path], git.Worktrees.Select(worktree => worktree.Path));
        Assert.Equal("/repos/shop", Assert.Single(git.Worktrees).Repository);
        Assert.Equal(git.Head.Value, info.BaseCommit);
        Assert.Equal([git.Head], git.CheckedOut);
    }

    [Fact]
    public async Task FindsAPreparedWorkspaceAsync()
    {
        var service = Service(new FakeGit());
        var prepared = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest("/repos/shop"), Cancellation));

        Assert.Equal(prepared, Outcomes.Succeeds(await service.FindAsync(prepared.Id, Cancellation)));
    }

    [Fact]
    public async Task RefusesAFolderOutsideARepositoryAsync()
    {
        var git = new FakeGit { InsideRepository = false };

        Assert.Equal(WorkspaceFailure.NotAGitRepository, Outcomes.FailsWith(await Service(git).PrepareAsync(new WorkspaceRequest("/tmp"), Cancellation)));
        Assert.Empty(git.Worktrees);
    }

    [Fact]
    public async Task RefusesABranchThatAlreadyExistsAsync()
    {
        var git = new FakeGit { BranchTaken = true };

        Assert.Equal(WorkspaceFailure.BranchAlreadyExists, Outcomes.FailsWith(await Service(git).PrepareAsync(new WorkspaceRequest("/repos/shop"), Cancellation)));
        Assert.Empty(git.Worktrees);
    }

    [Fact]
    public async Task ForgetsAWorkspaceGitCouldNotCreateAsync()
    {
        var store = new InMemoryWorkspaceStore();
        var git = new FakeGit { WorktreeFails = true };

        var failure = Outcomes.FailsWith(await Service(git, store).PrepareAsync(new WorkspaceRequest("/repos/shop"), Cancellation));

        Assert.Equal(WorkspaceFailure.GitFailed, failure);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task NumbersTheCheckpointsOfAWorkspaceAsync()
    {
        var service = Service(new FakeGit());
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest("/repos/shop"), Cancellation)).Id;

        var first = Outcomes.Succeeds(await service.CheckpointAsync(workspace, "turn 1", Cancellation));
        var second = Outcomes.Succeeds(await service.CheckpointAsync(workspace, "turn 2", Cancellation));

        Assert.Equal([(1, "turn 1"), (2, "turn 2")], new[] { first, second }.Select(checkpoint => (checkpoint.Number, checkpoint.Label)));
        Assert.NotEqual(first.Commit, second.Commit);
    }

    [Fact]
    public async Task RejectsAnUnknownWorkspaceAsync()
    {
        var service = Service(new FakeGit());
        var unknown = WorkspaceId.New();

        Assert.Equal(WorkspaceFailure.UnknownWorkspace, Outcomes.FailsWith(await service.FindAsync(unknown, Cancellation)));
        Assert.Equal(WorkspaceFailure.UnknownWorkspace, Outcomes.FailsWith(await service.CheckpointAsync(unknown, "turn 1", Cancellation)));
        Assert.Equal(WorkspaceFailure.UnknownWorkspace, Outcomes.FailsWith(await service.RemoveAsync(unknown, Cancellation)));
    }

    [Fact]
    public async Task RemovingDeletesTheWorktreeAndForgetsTheWorkspaceAsync()
    {
        var git = new FakeGit();
        var service = Service(git);
        var workspace = Outcomes.Succeeds(await service.PrepareAsync(new WorkspaceRequest("/repos/shop"), Cancellation)).Id;

        Assert.Equal(workspace, Outcomes.Succeeds(await service.RemoveAsync(workspace, Cancellation)));
        Assert.Empty(git.Worktrees);
        Assert.Empty(git.Branches);
        Assert.Equal(WorkspaceFailure.UnknownWorkspace, Outcomes.FailsWith(await service.CheckpointAsync(workspace, "late", Cancellation)));
    }

    private static WorkspaceService Service(FakeGit git, InMemoryWorkspaceStore? store = null) =>
        new(git, store ?? new InMemoryWorkspaceStore(), new WorkspaceSettings(Root));
}
