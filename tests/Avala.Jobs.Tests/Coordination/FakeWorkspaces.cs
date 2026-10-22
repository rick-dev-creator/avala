using System.Collections.Concurrent;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Tests.Coordination;

internal sealed class FakeWorkspaces : IWorkspaces, IWorkspaceCheckpoints
{
    private readonly ConcurrentDictionary<WorkspaceId, WorkspaceInfo> prepared = new();
    private readonly ConcurrentQueue<WorkspaceRequest> requests = new();
    private readonly ConcurrentQueue<(WorkspaceId Workspace, string Label)> checkpoints = new();
    private readonly ConcurrentQueue<int> restored = new();

    public Option<WorkspaceFailure> PreparationFailure { get; init; }

    public bool RemovalFails { get; set; }

    public IReadOnlyList<string> Requests => [.. requests.Select(request => request.RepositoryPath)];

    public IReadOnlyList<WorkspaceRequest> Prepared => [.. requests];

    public IReadOnlyList<string> Checkpoints => [.. checkpoints.Select(checkpoint => checkpoint.Label)];

    public IReadOnlyList<int> Restored => [.. restored];

    public IReadOnlyList<string> CheckpointsOf(WorkspaceId workspace) =>
        [.. checkpoints.Where(checkpoint => checkpoint.Workspace == workspace).Select(checkpoint => checkpoint.Label)];

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(WorkspaceRequest request, CancellationToken cancellationToken)
    {
        requests.Enqueue(request);

        if (PreparationFailure.IsSome)
        {
            return ValueTask.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Failure(PreparationFailure.Match(failure => failure, () => WorkspaceFailure.GitFailed)));
        }

        var id = WorkspaceId.New();
        var info = new WorkspaceInfo(id, $"/worktrees/{id.Value:N}", $"avala/{id.Value:N}", new string('0', 40))
        {
            RulesCommit = request.Rules.Match(rules => rules, () => new string('0', 40)),
            BaseBranch = "main",
        };
        prepared[id] = info;

        return ValueTask.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Success(info));
    }

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(prepared.GetValueOrDefault(workspace).ToOption().ToResult(WorkspaceFailure.UnknownWorkspace));

    public ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(WorkspaceId workspace, string label, CancellationToken cancellationToken)
    {
        checkpoints.Enqueue((workspace, label));

        return ValueTask.FromResult(Result<CheckpointInfo, WorkspaceFailure>.Success(
            new CheckpointInfo(workspace, checkpoints.Count, new string('a', 40), label)));
    }

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAtAsync(string folder, CancellationToken cancellationToken) =>
        ValueTask.FromResult(prepared.Values.FirstOrDefault(info => info.Path == folder).ToOption().ToResult(WorkspaceFailure.UnknownWorkspace));

    public ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new WorktreeReconciliation([], []));

    public ValueTask<WorktreeReconciliation> CleanAsync(WorktreeReconciliation found, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new WorktreeReconciliation([], []));

    public ValueTask<Result<WorkspaceId, WorkspaceFailure>> RemoveAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(RemovalFails ? Result<WorkspaceId, WorkspaceFailure>.Failure(WorkspaceFailure.GitFailed)
            : prepared.TryRemove(workspace, out _)
            ? Result<WorkspaceId, WorkspaceFailure>.Success(workspace)
            : Result<WorkspaceId, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace));

    public ValueTask<Result<IReadOnlyList<CheckpointChanges>, WorkspaceFailure>> ListAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<IReadOnlyList<CheckpointChanges>, WorkspaceFailure>.Success(
            [.. checkpoints.Select((checkpoint, index) => (checkpoint.Workspace, Change: new CheckpointChanges(index + 1, checkpoint.Label, new string('a', 40), [])))
                .Where(listed => listed.Workspace == workspace)
                .Select(listed => listed.Change)]));

    public ValueTask<Result<CheckpointChanges, WorkspaceFailure>> RestoreAsync(WorkspaceId workspace, int checkpoint, CancellationToken cancellationToken)
    {
        restored.Enqueue(checkpoint);

        return ValueTask.FromResult(Result<CheckpointChanges, WorkspaceFailure>.Success(new CheckpointChanges(checkpoint, string.Empty, new string('a', 40), [])));
    }
}
