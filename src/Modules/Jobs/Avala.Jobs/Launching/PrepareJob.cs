using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Sdk.Events;
using JobAnnouncement = Avala.Jobs.Contracts.JobSubmitted;

namespace Avala.Jobs.Launching;

internal sealed class PrepareJob(JobLedger ledger, JobLauncher launcher) : IHandle<JobAnnouncement>
{
    public async ValueTask HandleAsync(JobAnnouncement integrationEvent, CancellationToken cancellationToken) =>
        await ledger.FindAsync(integrationEvent.Job, cancellationToken).MatchAsync(
            job => launcher.LaunchAsync(job, cancellationToken),
            () => Task.CompletedTask);
}
