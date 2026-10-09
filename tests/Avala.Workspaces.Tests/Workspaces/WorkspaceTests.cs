using Avala.Testing;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Tests.Workspaces;

public sealed class WorkspaceTests
{
    private static readonly WorkspaceState[] ReachableStates =
        [WorkspaceState.Creating, WorkspaceState.Ready, WorkspaceState.Failed, WorkspaceState.Removed];

    private static readonly (string Name, WorkspaceState[] AllowedIn, WorkspaceError Rejection, Func<Workspace, WorkspaceError?> Apply)[] Operations =
    [
        ("MarkReady", [WorkspaceState.Creating], WorkspaceError.CannotMarkReady, workspace => Outcomes.ErrorOf(workspace.MarkReady())),
        ("Checkpoint", [WorkspaceState.Ready], WorkspaceError.CannotCheckpoint, workspace => Outcomes.ErrorOf(workspace.RecordCheckpoint(Given.Commit(1), "turn 1"))),
        ("Fail", [WorkspaceState.Creating], WorkspaceError.CannotFail, workspace => Outcomes.ErrorOf(workspace.Fail())),
        ("Remove", [WorkspaceState.Creating, WorkspaceState.Ready], WorkspaceError.CannotRemove, workspace => Outcomes.ErrorOf(workspace.Remove())),
    ];

    [Fact]
    public void AppliesOnlyTheTransitionsTheLifecycleAllows()
    {
        var mismatches =
            from state in ReachableStates
            from operation in Operations
            let workspace = Given.Workspace(state)
            let expected = operation.AllowedIn.Contains(state) ? (WorkspaceError?)null : operation.Rejection
            let actual = operation.Apply(workspace)
            where actual != expected || (expected is not null && workspace.State != state)
            select $"{operation.Name} in {state}: expected {expected?.ToString() ?? "success"}, got {actual?.ToString() ?? "success"}";

        Assert.Empty(mismatches);
    }

    [Fact]
    public void NumbersCheckpointsInOrder()
    {
        var workspace = Given.Workspace(WorkspaceState.Ready);

        Outcomes.Succeeds(workspace.RecordCheckpoint(Given.Commit(1), "turn 1"));
        Outcomes.Succeeds(workspace.RecordCheckpoint(Given.Commit(2), "turn 2"));

        Assert.Equal(
            [new Checkpoint(1, Given.Commit(1), "turn 1"), new Checkpoint(2, Given.Commit(2), "turn 2")],
            workspace.Checkpoints);
    }}
