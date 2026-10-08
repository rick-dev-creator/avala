using Avala.Jobs.Domain;
using Avala.Sdk;

namespace Avala.Jobs.Application;

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
