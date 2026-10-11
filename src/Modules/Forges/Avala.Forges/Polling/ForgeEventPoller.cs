using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Triggers.Contracts;

namespace Avala.Forges.Polling;

internal sealed record PolledTrigger(TriggerState State, ForgeRule Rule, string Forge)
{
    public string Signature => $"{Rule.Connection}|{Rule.Remote}|{Rule.Event}|{Rule.Branch.Match(branch => branch, () => string.Empty)}";

    public ForgeEventWatch Watch(EventWatchStatus status) => new(State.Id, new ForgeName(Rule.Connection), Rule.Event) { Forge = Forge, Status = status };
}

internal sealed class ForgeEventPoller(EventBook book, EventSource source, EventDispatch dispatch, TimeProvider clock)
    : IHandle<StartupCompleted>, IHandle<TriggersChanged>, IHandle<ForgeTriggerNudged>, IAsyncDisposable
{
    private readonly SerialExecutor executor = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<TriggerId, ITimer> timers = [];
    private readonly Dictionary<TriggerId, PolledTrigger> polled = [];
    private int disposed;

    public ValueTask HandleAsync(StartupCompleted integrationEvent, CancellationToken cancellationToken)
    {
        Post(SyncAsync);

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(TriggersChanged integrationEvent, CancellationToken cancellationToken)
    {
        Post(SyncAsync);

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(ForgeTriggerNudged integrationEvent, CancellationToken cancellationToken)
    {
        Post(token => PollAsync(integrationEvent.Trigger, token));

        return ValueTask.CompletedTask;
    }

    public Task<Result<ForgeEventWatch, ForgeError>> CheckNowAsync(TriggerId trigger, CancellationToken cancellationToken) =>
        executor.RunAsync(
            async token =>
            {
                if (!polled.ContainsKey(trigger))
                {
                    await SyncAsync(token);
                }

                await PollAsync(trigger, token);

                return book.WatchOf(trigger).ToResult(ForgeError.NotFound);
            },
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        await lifetime.CancelAsync();
        await executor.RunAsync(
            _ =>
            {
                foreach (var timer in timers.Values)
                {
                    timer.Dispose();
                }

                timers.Clear();

                return Task.CompletedTask;
            },
            CancellationToken.None);
        await executor.DisposeAsync();
        lifetime.Dispose();
    }

    private async Task SyncAsync(CancellationToken token)
    {
        var declared = await dispatch.ListAsync(token);

        foreach (var gone in polled.Keys.Where(trigger => declared.All(state => state.Id != trigger)).ToList())
        {
            Disarm(gone);
            polled.Remove(gone);
            await book.ForgetAsync(gone, token);
        }

        foreach (var state in declared)
        {
            await state.Forge.Match(rule => KeepAsync(state, rule, token), () => Task.CompletedTask);
        }
    }

    private async Task KeepAsync(TriggerState state, ForgeRule rule, CancellationToken token)
    {
        var trigger = new PolledTrigger(state, rule, await source.TitleAsync(rule, token));
        var known = polled.GetValueOrDefault(state.Id);
        polled[state.Id] = trigger;

        if (!state.Enabled)
        {
            Disarm(state.Id);
            await book.ShowAsync(trigger.Watch(EventWatchStatus.Disabled), token);
        }
        else if (known is null || !known.State.Enabled || known.Signature != trigger.Signature || !timers.ContainsKey(state.Id))
        {
            await book.ShowAsync(trigger.Watch(EventWatchStatus.Waiting), token);
            Arm(state.Id, clock.GetUtcNow());
        }
    }

    private async Task PollAsync(TriggerId id, CancellationToken token)
    {
        Disarm(id);

        if (!polled.TryGetValue(id, out var trigger) || !trigger.State.Enabled)
        {
            return;
        }

        var poll = await source.PollAsync(token);
        var now = clock.GetUtcNow();
        var cursor = book.CursorOf(id, trigger.Signature);
        var query = new ForgeEventQuery(trigger.Rule.Event, cursor.Map(found => found.Position), now) { Branch = trigger.Rule.Branch };
        var read = await (await source.ReaderAsync(trigger.State, trigger.Rule, token))
            .BindAsync(async reader => (await reader.ReadAsync(query, token)).Map(page => (Reader: reader, Page: page)));

        if (!read.TryGetValue(out var found, out var error))
        {
            var failures = book.WatchOf(id).Match(watch => watch.Failures, () => 0) + 1;
            var wait = Backoff.After(poll, failures);
            Arm(id, now + wait);
            await book.ShowAsync(trigger.Watch(EventWatchStatus.BackingOff) with { Failure = error, Failures = failures, LastPolled = now, NextPoll = now + wait, Baseline = cursor.Map(known => known.Baseline) }, token);

            return;
        }

        if (cursor.IsSome)
        {
            await HandleAsync(trigger, found.Reader, found.Page, now, token);
        }

        if (cursor.IsNone || found.Page.Events.Count <= ForgeEventQuery.DefaultMost)
        {
            await book.KeepCursorAsync(id, trigger.Signature, found.Page.Cursor, token);
        }
        Arm(id, now + poll);
        await book.ShowAsync(
            trigger.Watch(EventWatchStatus.Watching) with { LastPolled = now, NextPoll = now + poll, Baseline = book.CursorOf(id, trigger.Signature).Map(known => known.Baseline) },
            token);
    }

    private async Task HandleAsync(PolledTrigger trigger, EventReader reader, ForgeEventPage page, DateTimeOffset now, CancellationToken token)
    {
        foreach (var happened in page.Events.Take(ForgeEventQuery.DefaultMost))
        {
            var entry = await dispatch.HandleAsync(trigger.State, trigger.Rule, happened, now, token);
            await book.AddAsync(entry, token);

            if (entry.Verdict == ForgeEventVerdict.Fired && entry.Job.IsSome && trigger.Rule.Acknowledge)
            {
                await reader.AcknowledgeAsync(happened, $"Avala started a job for this: trigger {trigger.State.Id.Name}.", token);
            }

            await book.KeepCursorAsync(trigger.State.Id, trigger.Signature, happened.Cursor, token);
        }
    }

    private void Arm(TriggerId trigger, DateTimeOffset due)
    {
        Disarm(trigger);
        ITimer? alarm = null;
        alarm = clock.CreateAlarm(() => Post(token => RingAsync(trigger, alarm, token)), due);
        timers[trigger] = alarm;
    }

    private Task RingAsync(TriggerId trigger, ITimer? alarm, CancellationToken token) =>
        timers.TryGetValue(trigger, out var current) && ReferenceEquals(current, alarm) ? PollAsync(trigger, token) : Task.CompletedTask;

    private void Disarm(TriggerId trigger)
    {
        if (timers.Remove(trigger, out var timer))
        {
            timer.Dispose();
        }
    }

    private void Post(Func<CancellationToken, Task> work)
    {
        if (Volatile.Read(ref disposed) == 0)
        {
            _ = executor.RunAsync(work, lifetime.Token);
        }
    }
}
