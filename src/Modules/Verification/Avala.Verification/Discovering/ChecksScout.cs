using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Verification.Discovering;

internal sealed class ChecksScout(DiscoveryBook book, DiscoveryDesk desk, DiscoveredJobs jobs) : IHandle<JobProgressed>, IHandle<StartupCompleted>
{
    private static readonly JobStatus[] Ended = [JobStatus.Approved, JobStatus.Discarded, JobStatus.Failed];

    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken) =>
        await book.OfScout(integrationEvent.Job).Match(
            discovery => desk.ScoutMovedAsync(discovery, integrationEvent.Status, cancellationToken),
            () => integrationEvent.Status switch
            {
                JobStatus.Running => desk.StartAsync(integrationEvent.Job, cancellationToken),
                _ when Ended.Contains(integrationEvent.Status) => EndedAsync(integrationEvent.Job, cancellationToken),
                _ => Task.CompletedTask,
            });

    public async ValueTask HandleAsync(StartupCompleted integrationEvent, CancellationToken cancellationToken)
    {
        foreach (var discovery in book.Running())
        {
            var status = await jobs.StatusAsync(discovery.Job, cancellationToken);

            if (Ended.Contains(status))
            {
                await desk.AbandonAsync(discovery, cancellationToken);

                continue;
            }

            await ResumeAsync(discovery, cancellationToken);
        }
    }

    private async Task ResumeAsync(Discovery discovery, CancellationToken cancellationToken) =>
        await discovery.Scout.Match(
            async scout => await desk.ScoutMovedAsync(discovery, await jobs.StatusAsync(scout, cancellationToken), cancellationToken),
            () => discovery.Follows.IsSome ? desk.CatchUpAsync(discovery, cancellationToken) : desk.AbandonAsync(discovery, cancellationToken));

    private async Task EndedAsync(JobId job, CancellationToken cancellationToken) =>
        await book.Of(job).Match(
            discovery => discovery.Settled ? Task.CompletedTask : desk.AbandonAsync(discovery, cancellationToken),
            () => Task.CompletedTask);
}
