using System.Collections.Immutable;
using Avala.Sdk;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Triggers.Declarations;
using Avala.Triggers.Firing;

namespace Avala.Triggers.Scheduling;

internal sealed class TriggerScheduler(TriggerCatalog catalog, ScheduleBook book, TriggerFirer firer, RunJournal journal)
    : IStartupTask, IAsyncDisposable
{
    private const string Who = "schedule";

    private readonly SerialExecutor executor = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, ITimer> timers = [];
    private ImmutableDictionary<string, DateTimeOffset> next = ImmutableDictionary<string, DateTimeOffset>.Empty;
    private int disposed;

    public StartupStage Stage => StartupStage.Recovery;

    private TimeProvider Clock => journal.Clock;

    public Task RunAsync(CancellationToken cancellationToken) => ReloadAsync(cancellationToken);

    public Task<CatalogSnapshot> ReloadAsync(CancellationToken cancellationToken) =>
        executor.RunAsync(
            async token =>
            {
                var snapshot = await catalog.LoadAsync(token);
                await DisarmAllAsync();

                foreach (var trigger in snapshot.Triggers)
                {
                    await ArmAsync(trigger, ringing: false, token);
                }

                await journal.ChangedAsync(token);

                return snapshot;
            },
            cancellationToken);

    public Task EnableAsync(TriggerDeclaration trigger, bool enabled, CancellationToken cancellationToken) =>
        executor.RunAsync(
            async token =>
            {
                await book.KeepAsync(StateOf(trigger) with { Enabled = enabled }, token);
                await ArmAsync(trigger, ringing: false, token);
                await journal.ChangedAsync(token);
            },
            cancellationToken);

    public CatalogSnapshot Current => catalog.Current;

    public bool Enabled(TriggerDeclaration trigger) => book.Enabled(trigger);

    public Option<DateTimeOffset> NextOf(TriggerId trigger) =>
        Volatile.Read(ref next).TryGetValue(trigger.Key, out var at) ? at : Option<DateTimeOffset>.None;

    public Option<DateTimeOffset> LastOf(TriggerId trigger) => book.Of(trigger).Bind(state => state.LastFired);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        await lifetime.CancelAsync();
        await executor.RunAsync(_ => DisarmAllAsync(), CancellationToken.None);
        await executor.DisposeAsync();
        lifetime.Dispose();
    }

    private async Task ArmAsync(TriggerDeclaration trigger, bool ringing, CancellationToken token)
    {
        await DisarmAsync(trigger.Id);

        if (book.Enabled(trigger))
        {
            await trigger.Schedule.Match(schedule => ArmScheduledAsync(trigger, schedule, ringing, token), () => Task.CompletedTask);
        }
    }

    private async Task ArmScheduledAsync(TriggerDeclaration trigger, TriggerSchedule schedule, bool ringing, CancellationToken token)
    {
        var now = Clock.GetUtcNow();
        var state = book.Of(trigger.Id).Match(known => known, () => StateOf(trigger));
        var plan = ScheduleClock.Plan(schedule, state.From, now, Clock.LocalTimeZone);

        if (plan.Missed > 0 || book.Of(trigger.Id).IsNone)
        {
            state = await book.KeepAsync(state with { Anchor = plan.Missed > 0 ? plan.Anchor : state.Anchor }, token);
        }

        if (plan.Missed > 0)
        {
            await DueAsync(trigger, state, plan.Missed, ringing && plan.Missed == 1, token);
        }

        var id = trigger.Id;
        ImmutableInterlocked.Update(ref next, known => known.SetItem(id.Key, plan.Next));
        var wait = plan.Next - Clock.GetUtcNow();
        timers[id.Key] = Clock.CreateTimer(_ => Post(ring => RingAsync(id, ring)), null, wait > TimeSpan.Zero ? wait : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    private async Task DueAsync(TriggerDeclaration trigger, ScheduleState state, int missed, bool onTime, CancellationToken token)
    {
        if (onTime || trigger.CatchUp == CatchUp.Once)
        {
            _ = await firer.FireAsync(trigger, new FireRequest(onTime ? TriggerOrigin.Schedule : TriggerOrigin.CatchUp, Who), token);
            await book.KeepAsync(state with { LastFired = Clock.GetUtcNow() }, token);
        }
        else
        {
            _ = await firer.MissedAsync(trigger, missed, token);
        }
    }

    private async Task RingAsync(TriggerId trigger, CancellationToken token)
    {
        timers.Remove(trigger.Key);
        var current = await catalog.CurrentAsync(trigger, token);

        await current.Match(
            declared => ArmAsync(declared, ringing: true, token),
            () => DisarmAsync(trigger));
        await journal.ChangedAsync(token);
    }

    private ScheduleState StateOf(TriggerDeclaration trigger) =>
        book.Of(trigger.Id).Match(known => known, () => new ScheduleState(trigger.Id.Key, Clock.GetUtcNow()));

    private async Task DisarmAsync(TriggerId trigger)
    {
        ImmutableInterlocked.Update(ref next, known => known.Remove(trigger.Key));

        if (timers.Remove(trigger.Key, out var timer))
        {
            await timer.DisposeAsync();
        }
    }

    private async Task DisarmAllAsync()
    {
        foreach (var timer in timers.Values)
        {
            await timer.DisposeAsync();
        }

        timers.Clear();
        Volatile.Write(ref next, ImmutableDictionary<string, DateTimeOffset>.Empty);
    }

    private void Post(Func<CancellationToken, Task> work)
    {
        if (Volatile.Read(ref disposed) == 0)
        {
            _ = executor.RunAsync(work, lifetime.Token);
        }
    }
}
