using Avala.Jobs.Contracts;
using Avala.Jobs.Ledger;
using Avala.Sdk.Events;
using JobAnnouncement = Avala.Jobs.Contracts.JobSubmitted;

namespace Avala.Jobs.Launching;

internal sealed class PrepareJob(JobQueues queues, StartJob start, IEnumerable<IJobAdmission> admissions) : IHandle<JobAnnouncement>
{
    public async ValueTask HandleAsync(JobAnnouncement integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var admission in integrationEvent.Parent.IsSome ? Enumerable.Empty<IJobAdmission>() : admissions)
        {
            await admission.AdmitAsync(integrationEvent.Job, cancellationToken);
        }

        await queues.PostAsync(integrationEvent.Job, start.ExecuteAsync, cancellationToken);
    }
}
