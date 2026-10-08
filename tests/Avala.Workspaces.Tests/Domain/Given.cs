using Avala.Testing;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Domain;

namespace Avala.Workspaces.Tests.Domain;

internal static class Given
{
    public static CommitSha Commit(int number) => Outcomes.Succeeds(CommitSha.Create($"{number:x40}"));

    public static Workspace Workspace(WorkspaceState state = WorkspaceState.Creating)
    {
        var workspace = Outcomes.Succeeds(Avala.Workspaces.Domain.Workspace.Create(
            WorkspaceId.New(),
            Outcomes.Succeeds(WorkspaceLocation.Create("/repos/shop", "/worktrees/1")),
            Outcomes.Succeeds(BranchName.Create("avala/1"))));

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
