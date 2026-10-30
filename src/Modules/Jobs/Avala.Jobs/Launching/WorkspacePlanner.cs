using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Launching;

internal sealed class JobWorkspaces(IWorkspaces workspaces, IWorkspaceShelf shelf)
{
    public IWorkspaces Workspaces => workspaces;

    public async Task<Result<WorkspaceInfo, WorkspaceFailure>> ReadyAsync(Job job, CancellationToken cancellationToken) =>
        await workspaces.FindAsync(job, cancellationToken)
            .BindAsync(async found => found.Shelved ? await shelf.RebuildAsync(found.Id, cancellationToken) : found);
}

internal sealed class WorkspacePlanner(JobWorkspaces worktrees, IRepositoryDefaults defaults, JobQueues queues, ConnectionChooser chooser)
{

    public async Task<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(Job job, CancellationToken cancellationToken) =>
        await job.Parent.Match(
            parent => FromParentAsync(job, parent, cancellationToken),
            () => worktrees.Workspaces.PrepareAsync(new WorkspaceRequest(job.Repository.Value), cancellationToken).AsTask());

    public Task<Result<WorkspaceInfo, WorkspaceFailure>> ReadyAsync(Job job, CancellationToken cancellationToken) => worktrees.ReadyAsync(job, cancellationToken);

    public async Task<bool> RemoveAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
        (await worktrees.Workspaces.RemoveAsync(workspace, cancellationToken)).Match(_ => true, failure => failure == WorkspaceFailure.UnknownWorkspace);

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

        if (preferred.IsSome)
        {
            return preferred;
        }

        var parents = job.Companion ? await chooser.UsableAsync(await ParentConnectionAsync(job, cancellationToken), cancellationToken) : Option<ConnectionName>.None;

        return parents.IsSome ? parents : await chooser.ChooseAsync(job.Id, workspace.Path, cancellationToken);
    }

    private async Task<Option<ConnectionName>> ParentConnectionAsync(Job companion, CancellationToken cancellationToken) =>
        await companion.Parent.Match(
            async parent => (await queues.RunAsync(parent, (found, _) => Task.FromResult(found.Connection), cancellationToken)).Bind(connection => connection),
            () => Task.FromResult(Option<ConnectionName>.None));

    public async Task<ModelChoice> ModelOfAsync(Job job, WorkspaceInfo workspace, CancellationToken cancellationToken) =>
        job.Model.Or((await defaults.ModelAsync(workspace.Path, cancellationToken)).Match(declared => declared, _ => ModelChoice.Default));

    private async Task<Result<WorkspaceInfo, WorkspaceFailure>> FromParentAsync(Job child, JobId parent, CancellationToken cancellationToken) =>
        (await queues.RunAsync(parent, (found, token) => StartFromAsync(found, child, token), cancellationToken))
            .Match(prepared => prepared, () => Result<WorkspaceInfo, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace));

    private async Task<Result<WorkspaceInfo, WorkspaceFailure>> StartFromAsync(Job parent, Job child, CancellationToken cancellationToken)
    {
        if (!(await worktrees.Workspaces.FindAsync(parent, cancellationToken)).TryGetValue(out var origin, out var failure)
            || !(await worktrees.Workspaces.CheckpointAsync(parent, $"Delegated to job {child.Id.Value}", cancellationToken)).TryGetValue(out _, out failure))
        {
            return failure;
        }

        return await worktrees.Workspaces.PrepareAsync(new WorkspaceRequest(child.Repository.Value, origin.Branch) { Rules = origin.RulesCommit }, cancellationToken);
    }
}
