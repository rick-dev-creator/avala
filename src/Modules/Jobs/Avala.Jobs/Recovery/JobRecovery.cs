using Avala.Jobs.Ledger;
using Avala.Sdk;

namespace Avala.Jobs.Recovery;

internal sealed class JobRecovery(JobLedger ledger, JobQueues queues, RecoverJob recover) : IStartupTask
{
    public StartupStage Stage => StartupStage.Recovery;

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
                        await recover.ExecuteAsync(job, token);
                    }

                    return true;
                },
                cancellationToken);
        }
    }
}
