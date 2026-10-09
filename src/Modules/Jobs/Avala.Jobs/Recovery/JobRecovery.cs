using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Jobs.Ledger;
using Avala.Sdk;

namespace Avala.Jobs.Recovery;

internal sealed class JobRecovery(JobLedger ledger, JobQueues queues, JobLauncher launcher, DeferredJobs deferred) : IStartupTask
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var active in await ledger.ActiveAsync(cancellationToken))
        {
            _ = await queues.RunAsync(
                active.Id,
                async (job, token) =>
                {
                    if (!ledger.RecordedInThisRun(job.Id))
                    {
                        await RecoverAsync(job, token);
                    }

                    return true;
                },
                cancellationToken);
        }
    }

    private async Task RecoverAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.State == JobState.Preparing)
        {
            await launcher.LaunchAsync(job, cancellationToken);
        }
        else if (job.State != JobState.Running || !await deferred.DeferAsync(job.Id, cancellationToken))
        {
            await launcher.RelaunchAsync(job, cancellationToken);
        }
    }
}
