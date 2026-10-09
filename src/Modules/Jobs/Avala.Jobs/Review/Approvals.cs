using Avala.Jobs.Contracts;
using Avala.Jobs.Delivery;
using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Review;

internal sealed class Approvals(IWorkspaces workspaces, IRepositoryDefaults defaults, IEnumerable<IApprovalStrategy> strategies)
{
    public async Task<Result<ApprovalDelivery, JobRejection>> DeliverAsync(Job job, CancellationToken cancellationToken)
    {
        if (!(await workspaces.FindAsync(job, cancellationToken)).TryGetValue(out var workspace, out _))
        {
            return JobRejection.WorkspaceUnavailable;
        }

        if (!(await defaults.ApprovalAsync(workspace.Path, cancellationToken)).TryGetValue(out var named, out var rejection))
        {
            return rejection;
        }

        var name = named.Match(declared => declared, () => KeepStrategy.Keep);

        return await strategies.FirstOrDefault(strategy => strategy.Name == name).ToOption().Match(
            strategy => strategy.DeliverAsync(new ApprovalRequest(job.Id, job.Instruction.Text, job.Attempts.Count, workspace), cancellationToken).AsTask(),
            () => Task.FromResult(Result<ApprovalDelivery, JobRejection>.Failure(JobRejection.UnknownApprovalStrategy)));
    }
}
