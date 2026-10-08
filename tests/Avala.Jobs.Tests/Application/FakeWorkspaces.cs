using System.Collections.Concurrent;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Tests.Application;

internal sealed class FakeWorkspaces : IWorkspaces
{
    private readonly ConcurrentDictionary<WorkspaceId, WorkspaceInfo> prepared = new();
    private readonly ConcurrentQueue<string> requests = new();
    private readonly ConcurrentQueue<string> checkpoints = new();

    public bool PreparationFails { get; init; }

    public IReadOnlyList<string> Requests => [.. requests];

    public IReadOnlyList<string> Checkpoints => [.. checkpoints];

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(WorkspaceRequest request, CancellationToken cancellationToken)
    {
        requests.Enqueue(request.RepositoryPath);

        if (PreparationFails)
        {
            return ValueTask.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Failure(WorkspaceFailure.GitFailed));
        }

        var id = WorkspaceId.New();
        var info = new WorkspaceInfo(id, $"/worktrees/{id.Value:N}", $"avala/{id.Value:N}");
        prepared[id] = info;

        return ValueTask.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Success(info));
    }

    public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(prepared.GetValueOrDefault(workspace).ToOption().ToResult(WorkspaceFailure.UnknownWorkspace));

    public ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(WorkspaceId workspace, string label, CancellationToken cancellationToken)
    {
        checkpoints.Enqueue(label);

        return ValueTask.FromResult(Result<CheckpointInfo, WorkspaceFailure>.Success(
            new CheckpointInfo(workspace, checkpoints.Count, new string('a', 40), label)));
    }

    public ValueTask<Result<WorkspaceId, WorkspaceFailure>> RemoveAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        ValueTask.FromResult(prepared.TryRemove(workspace, out _)
            ? Result<WorkspaceId, WorkspaceFailure>.Success(workspace)
            : Result<WorkspaceId, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace));
}
