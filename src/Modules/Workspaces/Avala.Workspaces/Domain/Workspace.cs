using Avala.Sdk;
using Avala.Sdk.Domain;
using Avala.Workspaces.Contracts;
using Stateless;

namespace Avala.Workspaces.Domain;

internal sealed class Workspace : IAggregateRoot<WorkspaceId>
{
    private readonly List<Checkpoint> checkpoints = [];
    private readonly StateMachine<WorkspaceState, WorkspaceTrigger> machine;

    private Workspace(WorkspaceId id, WorkspaceLocation location, BranchName branch)
    {
        Id = id;
        Location = location;
        Branch = branch;
        machine = WorkspaceLifecycle.Create(() => State, state => State = state);
    }

    public WorkspaceId Id { get; }

    public WorkspaceLocation Location { get; }

    public BranchName Branch { get; }

    public WorkspaceState State { get; private set; } = WorkspaceState.Creating;

    public IReadOnlyList<Checkpoint> Checkpoints => checkpoints;

    public static Result<Workspace, WorkspaceError> Create(WorkspaceId id, WorkspaceLocation location, BranchName branch) =>
        new Workspace(id, location, branch);

    public Result<WorkspaceReady, WorkspaceError> MarkReady() =>
        machine.TryFire(WorkspaceTrigger.MarkReady, WorkspaceError.CannotMarkReady)
            .Map(_ => new WorkspaceReady(Id, Location, Branch));

    public Result<CheckpointRecorded, WorkspaceError> RecordCheckpoint(CommitSha commit, string label) =>
        machine.TryFire(WorkspaceTrigger.Checkpoint, WorkspaceError.CannotCheckpoint)
            .Map(_ =>
            {
                var checkpoint = new Checkpoint(checkpoints.Count + 1, commit, label);
                checkpoints.Add(checkpoint);

                return new CheckpointRecorded(Id, checkpoint);
            });

    public Result<WorkspaceFailed, WorkspaceError> Fail() =>
        machine.TryFire(WorkspaceTrigger.Fail, WorkspaceError.CannotFail)
            .Map(_ => new WorkspaceFailed(Id));

    public Result<WorkspaceRemoved, WorkspaceError> Remove() =>
        machine.TryFire(WorkspaceTrigger.Remove, WorkspaceError.CannotRemove)
            .Map(_ => new WorkspaceRemoved(Id));
}
