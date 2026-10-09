using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Launching;

internal sealed class WorkspacePlanner(IWorkspaces workspaces, IRepositoryDefaults defaults, JobQueues queues)
{
    public async Task<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(Job job, CancellationToken cancellationToken) =>
        await job.Parent.Match(
            parent => FromParentAsync(job, parent, cancellationToken),
            () => workspaces.PrepareAsync(new WorkspaceRequest(job.Repository.Value), cancellationToken).AsTask());

    public async Task<Result<Option<ConnectionName>, JobRejection>> ConnectionOfAsync(
        Job job,
        WorkspaceInfo workspace,
        CancellationToken cancellationToken) =>
        job.Connection.IsSome ? job.Connection : await defaults.ConnectionAsync(workspace.Path, cancellationToken);

    private async Task<Result<WorkspaceInfo, WorkspaceFailure>> FromParentAsync(Job child, JobId parent, CancellationToken cancellationToken) =>
        (await queues.RunAsync(parent, (found, token) => StartFromAsync(found, child, token), cancellationToken))
            .Match(prepared => prepared, () => Result<WorkspaceInfo, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace));

    private async Task<Result<WorkspaceInfo, WorkspaceFailure>> StartFromAsync(Job parent, Job child, CancellationToken cancellationToken)
    {
        if (!(await workspaces.FindAsync(parent, cancellationToken)).TryGetValue(out var origin, out var failure)
            || !(await workspaces.CheckpointAsync(parent, $"Delegated to job {child.Id.Value}", cancellationToken)).TryGetValue(out _, out failure))
        {
            return failure;
        }

        return await workspaces.PrepareAsync(new WorkspaceRequest(child.Repository.Value, origin.Branch) { Rules = origin.RulesCommit }, cancellationToken);
    }
}
