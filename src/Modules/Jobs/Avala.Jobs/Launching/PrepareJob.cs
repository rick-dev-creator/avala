using Avala.Jobs.Ledger;
using Avala.Sdk.Events;
using JobAnnouncement = Avala.Jobs.Contracts.JobSubmitted;

namespace Avala.Jobs.Launching;

internal sealed class PrepareJob(JobQueues queues, JobLauncher launcher) : IHandle<JobAnnouncement>
{
    public async ValueTask HandleAsync(JobAnnouncement integrationEvent, CancellationToken cancellationToken) =>
        await queues.PostAsync(integrationEvent.Job, launcher.LaunchAsync, cancellationToken);
}
