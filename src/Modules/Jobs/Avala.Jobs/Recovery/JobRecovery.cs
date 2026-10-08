using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Jobs.Ledger;
using Avala.Sdk;

namespace Avala.Jobs.Recovery;

internal sealed class JobRecovery(JobLedger ledger, JobLauncher launcher) : IStartupTask
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var job in await ledger.ActiveAsync(cancellationToken))
        {
            await (job.State == JobState.Preparing
                ? launcher.LaunchAsync(job, cancellationToken)
                : launcher.RelaunchAsync(job, cancellationToken));
        }
    }
}
