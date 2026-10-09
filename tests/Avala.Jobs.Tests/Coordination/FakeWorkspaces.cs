using System.Collections.Concurrent;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Tests.Coordination;

internal sealed class FakeWorkspaces : IWorkspaces
{
    private readonly ConcurrentDictionary<WorkspaceId, WorkspaceInfo> prepared = new();
    private readonly ConcurrentQueue<WorkspaceRequest> requests = new();
    private readonly ConcurrentQueue<(WorkspaceId Workspace, string Label)> checkpoints = new();

    public bool PreparationFails { get; init; }

    public IReadOnlyList<string> Requests => [.. requests.Select(request => request.RepositoryPath)];

    public IReadOnlyList<WorkspaceRequest> Prepared => [.. requests];

    public IReadOnlyList<string> Checkpoints => [.. checkpoints.Select(checkpoint => checkpoint.Label)];

    public IReadOnlyList<string> CheckpointsOf(WorkspaceId workspace) =>
        [.. checkpoints.Where(checkpoint => checkpoint.Workspace == workspace).Select(checkpoint => checkpoint.Label)];

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(WorkspaceRequest request, CancellationToken cancellationToken)
    {
        requests.Enqueue(request);

        if (PreparationFails)
        {
            return ValueTask.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Failure(WorkspaceFailure.GitFailed));
        }

        var id = WorkspaceId.New();
        var info = new WorkspaceInfo(id, $"/worktrees/{id.Value:N}", $"avala/{id.Value:N}", new string('0', 40))
        {
            RulesCommit = request.Rules.Match(rules => rules, () => new string('0', 40)),
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
        ValueTask.FromResult(prepared.TryRemove(workspace, out _)
            ? Result<WorkspaceId, WorkspaceFailure>.Success(workspace)
            : Result<WorkspaceId, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace));
}
