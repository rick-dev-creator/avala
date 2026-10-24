using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Verification.Discovering;

internal sealed class ScoutLauncher(IJobs jobs, IRoleSettings roles, IDiscoverySettings settings)
{
    public ValueTask<bool> DiscoversAsync(CancellationToken cancellationToken) => settings.DiscoversAsync(cancellationToken);

    public async Task<Result<JobId, JobRejection>> LaunchAsync(JobPlace place, CancellationToken cancellationToken)
    {
        var request = new JobRequest(place.Repository, ScoutBrief.Text)
        {
            Parent = place.Job,
            Autonomy = Autonomy.Autonomous,
            Companion = true,
            Model = await roles.OfRoleAsync(place.Worktree, ChildRole.Research, cancellationToken),
        };

        return await jobs.SubmitAsync(request, cancellationToken);
    }

    public async Task DiscardAsync(JobId scout, CancellationToken cancellationToken) => _ = await jobs.DiscardAsync(scout, cancellationToken);
}
