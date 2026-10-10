using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Triggers.Contracts;

namespace Avala.Triggers.Firing;

internal interface IRunStore
{
    Task<IReadOnlyList<TriggerRun>> RunsAsync(CancellationToken cancellationToken);

    Task KeepRunAsync(TriggerRun run, CancellationToken cancellationToken);

    Task<IReadOnlyList<WebhookDelivery>> DeliveriesAsync(CancellationToken cancellationToken);

    Task AddDeliveryAsync(WebhookDelivery delivery, CancellationToken cancellationToken);
}

internal sealed class RunJournal(IRunStore store, IEventBus bus, TimeProvider clock) : IStartupTask
{
    public const int RunsKept = 1000;

    public const int DeliveriesKept = 500;

    private ImmutableList<TriggerRun> runs = [];
    private ImmutableList<WebhookDelivery> deliveries = [];

    public TimeProvider Clock => clock;

    public IReadOnlyList<TriggerRun> Runs => Volatile.Read(ref runs);

    public IReadOnlyList<WebhookDelivery> Deliveries => Volatile.Read(ref deliveries);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var run in await store.RunsAsync(cancellationToken))
        {
            var kept = run.Outcome == RunOutcome.Enqueued && run.Job.IsNone ? run with { Outcome = RunOutcome.Dropped } : run;

            if (kept != run)
            {
                await store.KeepRunAsync(kept, cancellationToken);
            }

            Remember(kept);
        }

        foreach (var delivery in await store.DeliveriesAsync(cancellationToken))
        {
            ImmutableInterlocked.Update(ref deliveries, known => Bounded(known.Add(delivery), DeliveriesKept));
        }
    }

    public IReadOnlyList<TriggerRun> Of(TriggerId trigger) => [.. Runs.Where(run => run.Trigger == trigger)];

    public IReadOnlyList<JobId> JobsOf(TriggerId trigger) =>
        [.. Runs.Where(run => run.Trigger == trigger).SelectMany(run => run.Job.Match<IEnumerable<JobId>>(job => [job], () => []))];

    public async Task<TriggerRun> RecordAsync(TriggerRun run, CancellationToken cancellationToken)
    {
        await store.KeepRunAsync(run, cancellationToken);
        Remember(run);
        await bus.PublishAsync(new TriggerFired(run), cancellationToken);

        return run;
    }

    public async Task AttachAsync(Guid run, JobId job, CancellationToken cancellationToken)
    {
        foreach (var found in Runs.Where(known => known.Id == run).Take(1))
        {
            var attached = found with { Job = job };
            await store.KeepRunAsync(attached, cancellationToken);
            ImmutableInterlocked.Update(ref runs, known => known.Replace(found, attached));
            await ChangedAsync(cancellationToken);
        }
    }

    public async Task<WebhookDelivery> DeliveredAsync(WebhookDelivery delivery, CancellationToken cancellationToken)
    {
        await store.AddDeliveryAsync(delivery, cancellationToken);
        ImmutableInterlocked.Update(ref deliveries, known => Bounded(known.Add(delivery), DeliveriesKept));
        await bus.PublishAsync(new WebhookReceived(delivery), cancellationToken);

        return delivery;
    }

    public ValueTask ChangedAsync(CancellationToken cancellationToken) => bus.PublishAsync(new TriggersChanged(clock.GetUtcNow()), cancellationToken);

    private void Remember(TriggerRun run) =>
        ImmutableInterlocked.Update(ref runs, known => Bounded(known.Add(run), RunsKept));

    private static ImmutableList<T> Bounded<T>(ImmutableList<T> list, int most) =>
        list.Count > most ? list.RemoveRange(0, list.Count - most) : list;
}
