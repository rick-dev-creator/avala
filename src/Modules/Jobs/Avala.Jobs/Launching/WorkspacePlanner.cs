using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Launching;

internal sealed class WorkspacePlanner(IWorkspaces workspaces, IRepositoryDefaults defaults, JobQueues queues, ConnectionChooser chooser)
{
    public async Task<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(Job job, CancellationToken cancellationToken) =>
        await job.Parent.Match(
            parent => FromParentAsync(job, parent, cancellationToken),
            () => workspaces.PrepareAsync(new WorkspaceRequest(job.Repository.Value), cancellationToken).AsTask());

    public Task<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(Job job, CancellationToken cancellationToken) => workspaces.FindAsync(job, cancellationToken);

    public async Task<Result<Option<ConnectionName>, JobRejection>> ConnectionOfAsync(
        Job job,
        WorkspaceInfo workspace,
        CancellationToken cancellationToken)
    {
        if (job.Connection.IsSome)
        {
            return job.Connection;
        }

        if (!(await defaults.ConnectionAsync(workspace.Path, cancellationToken)).TryGetValue(out var preferred, out var rejection))
        {
            return rejection;
        }

        return preferred.IsSome ? preferred : await chooser.ChooseAsync(job.Id, workspace.Path, cancellationToken);
    }

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
