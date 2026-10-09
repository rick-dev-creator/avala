using Avala.Autopilot.Contracts;
using Avala.Autopilot.Evidence;
using Avala.Jobs.Contracts;
using Avala.Sdk.Events;

namespace Avala.Autopilot.Approving;

internal sealed class AutoApprover(EvidenceGatherer evidence, IJobs jobs, IEventBus bus, TimeProvider clock)
{
    public Task<JobEvidence> GatherAsync(JobId job, CancellationToken cancellationToken) => evidence.GatherAsync(job, cancellationToken);

    public async Task<AutoApproval> DecideAsync(LoopId loop, JobId job, JobEvidence gathered, CancellationToken cancellationToken)
    {
        var exceptions = gathered.Exceptions;
        var refused = new AutoApproval(loop, job, false, gathered.Summary, exceptions, clock.GetUtcNow());
        var decision = exceptions.Count > 0
            ? refused
            : (await jobs.ApproveAsync(job, cancellationToken)).Match(
                approval => refused with { Approved = true, Delivery = approval.Delivery, At = clock.GetUtcNow() },
                rejection => refused with { Exceptions = [ExceptionReason.DeliveryRefused], Refusal = rejection, At = clock.GetUtcNow() });
        await bus.PublishAsync(new AutoApprovalDecided(decision), cancellationToken);

        return decision;
    }
}
