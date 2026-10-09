using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Jobs.Ledger;
using Avala.Sdk;

namespace Avala.Jobs.Recovery;

internal sealed class JobRecovery(JobLedger ledger, JobQueues queues, JobLauncher launcher) : IStartupTask
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var active in await ledger.ActiveAsync(cancellationToken))
        {
            _ = await queues.RunAsync(
                active.Id,
                async (job, token) =>
                {
                    await (job.State == JobState.Preparing ? launcher.LaunchAsync(job, token) : launcher.RelaunchAsync(job, token));

                    return true;
                },
                cancellationToken);
        }
    }
}
