using Avala.Testing;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Tests.Workspaces;

internal static class Given
{
    public static CommitSha Commit(int number) => Outcomes.Succeeds(CommitSha.Create($"{number:x40}"));

    public static Workspace Workspace(WorkspaceState state = WorkspaceState.Creating, string path = "/worktrees/1")
    {
        var workspace = Outcomes.Succeeds(Avala.Workspaces.Workspaces.Workspace.Create(
            WorkspaceId.New(),
            Outcomes.Succeeds(WorkspaceLocation.Create("/repos/shop", path)),
            Outcomes.Succeeds(BranchName.Create("avala/1")),
            Commit(0),
            Outcomes.Succeeds(BranchName.Create("main"))));

        var error = state switch
        {
            WorkspaceState.Ready => Outcomes.ErrorOf(workspace.MarkReady()),
            WorkspaceState.Failed => Outcomes.ErrorOf(workspace.Fail()),
            WorkspaceState.Removed => Outcomes.ErrorOf(workspace.Remove()),
            _ => null,
        };

        Assert.Null(error);

        return workspace;
    }
}
