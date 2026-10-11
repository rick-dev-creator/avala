using System.Collections.Immutable;
using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Triggers.Contracts;

namespace Avala.Forges.Polling;

internal sealed record EventCursor(string Trigger, string Rule, string Position, DateTimeOffset Baseline);

internal interface IEventStore
{
    Task<IReadOnlyList<EventCursor>> CursorsAsync(CancellationToken cancellationToken);

    Task KeepCursorAsync(EventCursor cursor, CancellationToken cancellationToken);

    Task<IReadOnlyList<ForgeEventEntry>> EntriesAsync(CancellationToken cancellationToken);

    Task AddEntryAsync(ForgeEventEntry entry, CancellationToken cancellationToken);
}

internal sealed class EventBook(IEventStore store, IEventBus bus, TimeProvider clock) : IStartupTask
{
    public const int EntriesKept = 200;

    private ImmutableDictionary<string, EventCursor> cursors = ImmutableDictionary<string, EventCursor>.Empty;
    private ImmutableDictionary<TriggerId, ForgeEventWatch> watches = ImmutableDictionary<TriggerId, ForgeEventWatch>.Empty;
    private ImmutableList<ForgeEventEntry> entries = [];

    public IReadOnlyList<ForgeEventWatch> Watches => [.. Volatile.Read(ref watches).Values.OrderBy(watch => watch.Trigger.Key, StringComparer.Ordinal)];

    public IReadOnlyList<ForgeEventEntry> Entries => Volatile.Read(ref entries);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref cursors, (await store.CursorsAsync(cancellationToken)).ToImmutableDictionary(cursor => cursor.Trigger, StringComparer.Ordinal));
        Volatile.Write(ref entries, Bounded([.. await store.EntriesAsync(cancellationToken)]));
    }

    public Option<EventCursor> CursorOf(TriggerId trigger, string rule) =>
        Volatile.Read(ref cursors).TryGetValue(trigger.Key, out var cursor) && cursor.Rule == rule ? cursor : Option<EventCursor>.None;

    public Option<ForgeEventWatch> WatchOf(TriggerId trigger) =>
        Volatile.Read(ref watches).TryGetValue(trigger, out var watch) ? watch : Option<ForgeEventWatch>.None;

    public async Task KeepCursorAsync(TriggerId trigger, string rule, string position, CancellationToken cancellationToken)
    {
        var cursor = CursorOf(trigger, rule).Match(known => known with { Position = position }, () => new EventCursor(trigger.Key, rule, position, clock.GetUtcNow()));
        await store.KeepCursorAsync(cursor, cancellationToken);
        ImmutableInterlocked.Update(ref cursors, known => known.SetItem(cursor.Trigger, cursor));
    }

    public async Task AddAsync(ForgeEventEntry entry, CancellationToken cancellationToken)
    {
        await store.AddEntryAsync(entry, cancellationToken);
        ImmutableInterlocked.Update(ref entries, known => Bounded(known.Add(entry)));
        await ChangedAsync(cancellationToken);
    }

    public async Task ShowAsync(ForgeEventWatch watch, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref watches, known => known.SetItem(watch.Trigger, watch));
        await ChangedAsync(cancellationToken);
    }

    public async Task ForgetAsync(TriggerId trigger, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref watches, known => known.Remove(trigger));
        await ChangedAsync(cancellationToken);
    }

    private ValueTask ChangedAsync(CancellationToken cancellationToken) => bus.PublishAsync(new ForgeEventsChanged(clock.GetUtcNow()), cancellationToken);

    private static ImmutableList<ForgeEventEntry> Bounded(ImmutableList<ForgeEventEntry> list) =>
        list.Count > EntriesKept ? list.RemoveRange(0, list.Count - EntriesKept) : list;
}
