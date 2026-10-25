using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Jobs.Ledger;
using Avala.Sdk.Events;

namespace Avala.Jobs.TurnChecks;

internal sealed class RecheckJob(JobQueues queues, JobLedger ledger, EvaluateTurn evaluate, JobLauncher launcher) : IHandle<DeferredGateReady>
{
    public async ValueTask HandleAsync(DeferredGateReady integrationEvent, CancellationToken cancellationToken) =>
        await queues.PostAsync(integrationEvent.Job, (found, token) => found.State == JobState.Checking ? RecheckAsync(found, token) : Task.CompletedTask, cancellationToken);

    private async Task RecheckAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.Recheck().IsSuccess)
        {
            await ledger.RecordAsync(job, cancellationToken);
            await evaluate.JudgeAsync(job, launcher.RetryInLiveSessionAsync, cancellationToken);
        }
    }
}
