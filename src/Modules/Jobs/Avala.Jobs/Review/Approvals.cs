using Avala.Jobs.Contracts;
using Avala.Jobs.Delivery;
using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Review;

internal sealed class Approvals(IWorkspaces workspaces, IRepositoryDefaults defaults, IEnumerable<IApprovalStrategy> strategies, JobQueues queues)
{
    public async Task<Result<ApprovalDelivery, JobRejection>> DeliverAsync(Job job, CancellationToken cancellationToken)
    {
        if (!(await workspaces.FindAsync(job, cancellationToken)).TryGetValue(out var workspace, out _))
        {
            return JobRejection.WorkspaceUnavailable;
        }

        var request = new ApprovalRequest(job.Id, job.Instruction.Text, job.Attempts.Count, workspace);

        return await job.Parent.Match(
            parent => IntoParentAsync(parent, request, cancellationToken),
            () => ThroughRepositoryStrategyAsync(request, cancellationToken));
    }

    private async Task<Result<ApprovalDelivery, JobRejection>> ThroughRepositoryStrategyAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        if (!(await defaults.ApprovalAsync(request.Workspace.Path, cancellationToken)).TryGetValue(out var named, out var rejection))
        {
            return rejection;
        }

        return await DeliverThroughAsync(named.Match(declared => declared, () => KeepStrategy.Keep), request, cancellationToken);
    }

    private async Task<Result<ApprovalDelivery, JobRejection>> IntoParentAsync(JobId parent, ApprovalRequest request, CancellationToken cancellationToken) =>
        (await queues.RunAsync(parent, (found, token) => IntegrateAsync(found, request, token), cancellationToken))
            .Match(delivered => delivered, () => Result<ApprovalDelivery, JobRejection>.Failure(JobRejection.UnknownParent));

    private async Task<Result<ApprovalDelivery, JobRejection>> IntegrateAsync(Job parent, ApprovalRequest request, CancellationToken cancellationToken)
    {
        if (parent.State is not (JobState.Running or JobState.NeedsHelp))
        {
            return JobRejection.ParentNotRunning;
        }

        if ((await workspaces.CheckpointAsync(parent, $"Before integrating job {request.Job.Value}", cancellationToken)).IsFailure)
        {
            return JobRejection.WorkspaceUnavailable;
        }

        return await DeliverThroughAsync(MergeStrategy.Merge, request, cancellationToken);
    }

    private async Task<Result<ApprovalDelivery, JobRejection>> DeliverThroughAsync(string name, ApprovalRequest request, CancellationToken cancellationToken) =>
        await strategies.FirstOrDefault(strategy => strategy.Name == name).ToOption().Match(
            strategy => strategy.DeliverAsync(request, cancellationToken).AsTask(),
            () => Task.FromResult(Result<ApprovalDelivery, JobRejection>.Failure(JobRejection.UnknownApprovalStrategy)));
}
