using System.Collections.Immutable;
using Avala.Sdk;
using Avala.Triggers.Contracts;
using Avala.Triggers.Declarations;

namespace Avala.Triggers.Scheduling;

internal sealed record ScheduleState(string Trigger, DateTimeOffset Since)
{
    public Option<DateTimeOffset> Anchor { get; init; }

    public Option<DateTimeOffset> LastFired { get; init; }

    public Option<bool> Enabled { get; init; }

    public DateTimeOffset From => Anchor.Match(anchor => anchor, () => Since);
}

internal interface IScheduleStore
{
    Task<IReadOnlyList<ScheduleState>> SchedulesAsync(CancellationToken cancellationToken);

    Task KeepAsync(ScheduleState state, CancellationToken cancellationToken);
}

internal sealed class ScheduleBook(IScheduleStore store) : IStartupTask
{
    private ImmutableDictionary<string, ScheduleState> states = ImmutableDictionary<string, ScheduleState>.Empty;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var state in await store.SchedulesAsync(cancellationToken))
        {
            ImmutableInterlocked.Update(ref states, known => known.SetItem(state.Trigger, state));
        }
    }

    public Option<ScheduleState> Of(TriggerId trigger) =>
        Volatile.Read(ref states).TryGetValue(trigger.Key, out var state) ? state : Option<ScheduleState>.None;

    public bool Enabled(TriggerDeclaration trigger) =>
        Of(trigger.Id).Bind(state => state.Enabled).Match(enabled => enabled, () => trigger.Enabled);

    public async Task<ScheduleState> KeepAsync(ScheduleState state, CancellationToken cancellationToken)
    {
        await store.KeepAsync(state, cancellationToken);
        ImmutableInterlocked.Update(ref states, known => known.SetItem(state.Trigger, state));

        return state;
    }
}
