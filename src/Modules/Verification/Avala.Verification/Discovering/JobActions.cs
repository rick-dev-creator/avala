using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Verification.Discovering;

internal sealed class JobActions(IJobs jobs, IRoleSettings roles, IEventBus bus)
{
    public async Task<Result<JobId, JobRejection>> LaunchAsync(JobPlace place, bool runs, CancellationToken cancellationToken)
    {
        var request = new JobRequest(place.Repository, ScoutBrief.Text(runs))
        {
            Parent = place.Job,
            Autonomy = Autonomy.Autonomous,
            Companion = true,
            Model = await roles.OfRoleAsync(place.Worktree, ChildRole.Research, cancellationToken),
        };

        return await jobs.SubmitAsync(request, cancellationToken);
    }

    public async Task DiscardAsync(JobId scout, CancellationToken cancellationToken) => _ = await jobs.DiscardAsync(scout, cancellationToken);

    public async Task RecheckAsync(JobId job, CancellationToken cancellationToken) => await bus.PublishAsync(new DeferredGateReady(job), cancellationToken);
}
