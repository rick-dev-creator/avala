using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Workbench.Steering;

internal sealed class QueuedMessages(IJobs jobs) : IHandle<JobProgressed>
{
    private ImmutableDictionary<JobId, string> queued = ImmutableDictionary<JobId, string>.Empty;

    public Option<string> Find(JobId job) => Volatile.Read(ref queued).GetValueOrDefault(job).ToOption();

    public void Queue(JobId job, string message) =>
        ImmutableInterlocked.AddOrUpdate(ref queued, job, message, (_, earlier) => $"{earlier}\n\n{message}");

    public bool Withdraw(JobId job) => ImmutableInterlocked.TryRemove(ref queued, job, out _);

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        var job = integrationEvent.Job;

        if (integrationEvent.Status.IsOver)
        {
            Withdraw(job);
            return;
        }

        if (!integrationEvent.Status.AcceptsMessages || !ImmutableInterlocked.TryRemove(ref queued, job, out var message))
        {
            return;
        }

        var delivered = integrationEvent.Status == JobStatus.NeedsHelp
            ? await jobs.ContinueAsync(job, message, cancellationToken)
            : await jobs.SendBackAsync(job, message, cancellationToken);

        if (!delivered.IsSuccess)
        {
            Queue(job, message);
        }
    }
}
