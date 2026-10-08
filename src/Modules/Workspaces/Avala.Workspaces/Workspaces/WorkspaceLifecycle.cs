using Stateless;

namespace Avala.Workspaces.Workspaces;

internal static class WorkspaceLifecycle
{
    public static StateMachine<WorkspaceState, WorkspaceTrigger> Create(Func<WorkspaceState> read, Action<WorkspaceState> write)
    {
        var machine = new StateMachine<WorkspaceState, WorkspaceTrigger>(read, write);

        machine.Configure(WorkspaceState.Live)
            .Permit(WorkspaceTrigger.Remove, WorkspaceState.Removed);

        machine.Configure(WorkspaceState.Creating)
            .SubstateOf(WorkspaceState.Live)
            .Permit(WorkspaceTrigger.MarkReady, WorkspaceState.Ready)
            .Permit(WorkspaceTrigger.Fail, WorkspaceState.Failed);

        machine.Configure(WorkspaceState.Ready)
            .SubstateOf(WorkspaceState.Live)
            .InternalTransition(WorkspaceTrigger.Checkpoint, () => { });

        return machine;
    }
}
