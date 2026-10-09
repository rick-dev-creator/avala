using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Delegation.Resuming;

internal sealed class ParentResumption(DeferredParents parents, IJobs jobs) : IHandle<ChildReported>, IHandle<StartupCompleted>
{
    public async ValueTask HandleAsync(ChildReported integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var parent in integrationEvent.Delegation.Parent.Match<JobId[]>(found => [found], () => []))
        {
            await ResumeWhenAnsweredAsync(parent, cancellationToken);
        }
    }

    public async ValueTask HandleAsync(StartupCompleted integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var parent in parents.Waiting.ToList())
        {
            await ResumeWhenAnsweredAsync(parent, cancellationToken);
        }
    }

    private async Task ResumeWhenAnsweredAsync(JobId parent, CancellationToken cancellationToken)
    {
        if (parents.Answered(parent))
        {
            _ = await jobs.ResumeAsync(parent, cancellationToken);
        }
    }
}
