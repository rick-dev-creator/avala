using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Storage;
using Avala.Workspaces.Tests.Workspaces;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Tests.Storage;

public sealed class SqliteWorkspaceStoreTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AWorkspaceSurvivesAReloadAsync()
    {
        using var folder = new TemporaryFolder();
        var workspace = Given.Workspace(WorkspaceState.Ready);
        Outcomes.Succeeds(workspace.RecordCheckpoint(Given.Commit(1), "Attempt 1"));

        await using (var store = new SqliteWorkspaceStore(new AvalaPaths(folder.Path)))
        {
            await store.SaveAsync(workspace, Cancellation);
            Outcomes.Succeeds(workspace.RecordCheckpoint(Given.Commit(2), "Attempt 2"));
            await store.SaveAsync(workspace, Cancellation);
        }

        await using var reloaded = new SqliteWorkspaceStore(new AvalaPaths(folder.Path));
        var found = (await reloaded.FindAsync(workspace.Id, Cancellation)).Match(stored => stored, () => throw new InvalidOperationException("Not stored"));

        Assert.Equal(
            (workspace.Location, workspace.Branch, workspace.State),
            (found.Location, found.Branch, found.State));
        Assert.Equal(workspace.Checkpoints, found.Checkpoints);
    }

    [Fact]
    public async Task SavingAWorkspaceLeavesTheUnsavedChangesOfAnotherWorkspaceUnstoredAsync()
    {
        using var folder = new TemporaryFolder();
        var saved = Given.Workspace(WorkspaceState.Ready);
        var changing = Given.Workspace(WorkspaceState.Ready);
        await using (var store = new SqliteWorkspaceStore(new AvalaPaths(folder.Path)))
        {
            await store.SaveAsync(saved, Cancellation);
            await store.SaveAsync(changing, Cancellation);
            Outcomes.Succeeds(changing.RecordCheckpoint(Given.Commit(1), "Attempt 1"));
            Outcomes.Succeeds(saved.RecordCheckpoint(Given.Commit(2), "Attempt 1"));
            await store.SaveAsync(saved, Cancellation);
        }

        await using var reloaded = new SqliteWorkspaceStore(new AvalaPaths(folder.Path));
        Assert.Single((await reloaded.FindAsync(saved.Id, Cancellation)).Match(found => found.Checkpoints, () => []));
        Assert.Empty((await reloaded.FindAsync(changing.Id, Cancellation)).Match(found => found.Checkpoints, () => []));
    }

    [Fact]
    public async Task ARemovedWorkspaceIsGoneAsync()
    {
        using var folder = new TemporaryFolder();
        var workspace = Given.Workspace(WorkspaceState.Ready);
        await using var store = new SqliteWorkspaceStore(new AvalaPaths(folder.Path));
        await store.SaveAsync(workspace, Cancellation);

        await store.RemoveAsync(workspace.Id, Cancellation);

        Assert.True((await store.FindAsync(workspace.Id, Cancellation)).IsNone);
    }
}
