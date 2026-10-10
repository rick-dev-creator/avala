using System.Collections.Immutable;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Forges.Watching;

internal sealed record KeptWatch(PullRequestWatchState State, string Remote, string RemoteUrl, IReadOnlyList<string> Handled)
{
    public bool IsLive => State.Status != WatchStatus.Ended;
}

internal interface IWatchStore
{
    Task<IReadOnlyList<KeptWatch>> WatchesAsync(CancellationToken cancellationToken);

    Task KeepAsync(KeptWatch watch, CancellationToken cancellationToken);

    Task<IReadOnlyList<WakeUpRecord>> WakeUpsAsync(CancellationToken cancellationToken);

    Task AddAsync(WakeUpRecord wakeUp, CancellationToken cancellationToken);
}

internal sealed class WatchBook(IWatchStore store, IEventBus bus) : IStartupTask
{
    private ImmutableDictionary<JobId, KeptWatch> watches = ImmutableDictionary<JobId, KeptWatch>.Empty;
    private ImmutableDictionary<JobId, ForgeError> refusals = ImmutableDictionary<JobId, ForgeError>.Empty;
    private ImmutableDictionary<JobId, ImmutableList<WakeUpRecord>> wakeUps = ImmutableDictionary<JobId, ImmutableList<WakeUpRecord>>.Empty;

    public IReadOnlyList<KeptWatch> Live => [.. Volatile.Read(ref watches).Values.Where(watch => watch.IsLive)];

    public Option<KeptWatch> Find(JobId job) => Volatile.Read(ref watches).TryGetValue(job, out var kept) ? kept : Option<KeptWatch>.None;

    public IReadOnlyList<WakeUpRecord> WakeUpsOf(JobId job) => Volatile.Read(ref wakeUps).GetValueOrDefault(job, []);

    public Option<ForgeError> RefusalOf(JobId job) => Volatile.Read(ref refusals).TryGetValue(job, out var error) ? error : Option<ForgeError>.None;

    public async Task RefusedAsync(JobId job, ForgeError error, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref refusals, known => known.SetItem(job, error));
        await bus.PublishAsync(new PullRequestNotOpened(job, error), cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var kept in await store.WatchesAsync(cancellationToken))
        {
            ImmutableInterlocked.Update(ref watches, known => known.SetItem(kept.State.Job, kept));
        }

        foreach (var wakeUp in await store.WakeUpsAsync(cancellationToken))
        {
            Remember(wakeUp);
        }
    }

    public async Task SaveAsync(KeptWatch kept, CancellationToken cancellationToken)
    {
        await store.KeepAsync(kept, cancellationToken);
        ImmutableInterlocked.Update(ref refusals, known => known.Remove(kept.State.Job));
        ImmutableInterlocked.Update(ref watches, known => known.SetItem(kept.State.Job, kept));
        await bus.PublishAsync(new PullRequestWatchChanged(kept.State), cancellationToken);
    }

    public async Task AddAsync(WakeUpRecord wakeUp, CancellationToken cancellationToken)
    {
        await store.AddAsync(wakeUp, cancellationToken);
        Remember(wakeUp);
        await bus.PublishAsync(new PullRequestWakeUp(wakeUp), cancellationToken);
    }

    public ValueTask PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent =>
        bus.PublishAsync(integrationEvent, cancellationToken);

    private void Remember(WakeUpRecord wakeUp) =>
        ImmutableInterlocked.Update(ref wakeUps, known => known.SetItem(wakeUp.Job, known.GetValueOrDefault(wakeUp.Job, []).Add(wakeUp)));
}
