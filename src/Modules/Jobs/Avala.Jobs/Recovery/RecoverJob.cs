using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Jobs.Ledger;
using Avala.Jobs.TurnChecks;

namespace Avala.Jobs.Recovery;

internal sealed class RecoverJob(JobLedger ledger, JobLauncher launcher, EvaluateTurn evaluate, DeferredJobs deferred)
{
    public async Task ExecuteAsync(Job job, CancellationToken cancellationToken)
    {
        switch (job.State)
        {
            case JobState.Preparing:
                await launcher.LaunchAsync(job, cancellationToken);
                break;
            case JobState.Checking:
                await RecheckAsync(job, cancellationToken);
                break;
            case JobState.Running when !await deferred.DeferAsync(job.Id, cancellationToken):
                await launcher.RelaunchAsync(job, cancellationToken);
                break;
        }
    }

    private async Task RecheckAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.Recheck().IsSuccess)
        {
            await ledger.RecordAsync(job, cancellationToken);
            await evaluate.JudgeAsync(job, launcher.RetryInNewSessionAsync, cancellationToken);
        }
    }
}
