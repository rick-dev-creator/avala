using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Watching;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Handoffs.Records;

internal sealed class HandoffBook(IHandoffStore store, IUsage usage, IUsageSessions sessions, IEventBus bus) : IHandoffs, IHandle<JobHandedOff>, IStartupTask
{
    private ImmutableDictionary<JobId, ImmutableList<HandoffRecord>> handoffs = ImmutableDictionary<JobId, ImmutableList<HandoffRecord>>.Empty;
    private ImmutableDictionary<JobId, ResetWait> waits = ImmutableDictionary<JobId, ResetWait>.Empty;

    public IReadOnlyList<HandoffRecord> OfJob(JobId job) => Volatile.Read(ref handoffs).GetValueOrDefault(job, []);

    public Option<ResetWait> WaitOf(JobId job) =>
        Volatile.Read(ref waits).TryGetValue(job, out var wait) ? wait : Option<ResetWait>.None;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var handoff in await store.HandoffsAsync(cancellationToken))
        {
            Remember(handoff);
        }
    }

    public async ValueTask HandleAsync(JobHandedOff integrationEvent, CancellationToken cancellationToken)
    {
        var why = integrationEvent.Choice.Compared
            .Where(candidate => candidate.Connection == integrationEvent.From)
            .Select(from => from.Window.Map(window => new LimitReason(from.Connection, window.Window, from.Used, from.Threshold)))
            .FirstOrDefault();
        var spent = sessions.Sessions()
            .Where(session => session.Job == Option<JobId>.Some(integrationEvent.Job) && session.Connection == integrationEvent.From)
            .SelectMany(session => usage.OfSession(session.Session).Match<UsageSummary[]>(summary => [summary], () => []))
            .ToList();
        var handoff = new HandoffRecord(integrationEvent.Job, integrationEvent.Attempt, integrationEvent.From, integrationEvent.To, why, integrationEvent.Choice.At)
        {
            Spent = [.. spent.SelectMany(summary => summary.Costs).GroupBy(cost => cost.Currency, StringComparer.Ordinal).Select(currency => new Cost(currency.Sum(cost => cost.Amount), currency.Key))],
            Tokens = spent.Sum(summary => summary.Tokens.Input + summary.Tokens.Output + summary.Tokens.CacheRead + summary.Tokens.CacheWrite + summary.Tokens.Reasoning),
        };

        await store.AddAsync(handoff, cancellationToken);
        Remember(handoff);
        await bus.PublishAsync(new HandoffRecorded(handoff), cancellationToken);
    }

    public async Task WaitsAsync(ResetWait wait, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref waits, known => known.SetItem(wait.Job, wait));
        await bus.PublishAsync(new JobWaitsForReset(wait), cancellationToken);
    }

    public void Ended(JobId job) => ImmutableInterlocked.Update(ref waits, known => known.Remove(job));

    private void Remember(HandoffRecord handoff) =>
        ImmutableInterlocked.Update(ref handoffs, known => known.SetItem(handoff.Job, known.GetValueOrDefault(handoff.Job, []).Add(handoff)));
}
