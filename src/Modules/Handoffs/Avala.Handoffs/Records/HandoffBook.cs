using System.Collections.Immutable;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Handoffs.Records;

internal sealed class HandoffBook(IHandoffStore store, JobSpending spending, IConnections connections, IEventBus bus) : IHandoffs, IHandle<JobHandedOff>, IStartupTask
{
    private ImmutableDictionary<JobId, ImmutableList<HandoffRecord>> handoffs = ImmutableDictionary<JobId, ImmutableList<HandoffRecord>>.Empty;
    private ImmutableDictionary<JobId, ResetWait> waits = ImmutableDictionary<JobId, ResetWait>.Empty;
    private ImmutableDictionary<JobId, ImmutableList<RelayMark>> marks = ImmutableDictionary<JobId, ImmutableList<RelayMark>>.Empty;

    public IReadOnlyList<HandoffRecord> OfJob(JobId job) => Volatile.Read(ref handoffs).GetValueOrDefault(job, []);

    public Option<ResetWait> WaitOf(JobId job) =>
        Volatile.Read(ref waits).TryGetValue(job, out var wait) ? wait : Option<ResetWait>.None;

    public IReadOnlyList<RelayMark> MarksOf(JobId job) => Volatile.Read(ref marks).GetValueOrDefault(job, []);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var handoff in await store.HandoffsAsync(cancellationToken))
        {
            Remember(handoff);
        }

        foreach (var mark in await store.MarksAsync(cancellationToken))
        {
            Remember(mark);
        }
    }

    public async Task MarkAsync(RelayMark mark, CancellationToken cancellationToken)
    {
        await store.MarkAsync(mark, cancellationToken);
        Remember(mark);
        await bus.PublishAsync(new RelayMarked(mark), cancellationToken);
    }

    public async ValueTask HandleAsync(JobHandedOff integrationEvent, CancellationToken cancellationToken)
    {
        var why = integrationEvent.Choice.Compared
            .Where(candidate => candidate.Connection == integrationEvent.From)
            .Select(from => from.Window.Map(window => new LimitReason(from.Connection, window.Window, from.Used, from.Threshold)))
            .FirstOrDefault();
        var (costs, tokens) = spending.On(integrationEvent.Job, integrationEvent.From);
        var handoff = new HandoffRecord(integrationEvent.Job, integrationEvent.Attempt, integrationEvent.From, integrationEvent.To, why, integrationEvent.Choice.At)
        {
            Spent = costs,
            Tokens = tokens,
            Model = integrationEvent.ModelFellBack
                ? new ModelFallback(integrationEvent.Wanted, await DefaultsOfAsync(integrationEvent.To, cancellationToken))
                : Option<ModelFallback>.None,
        };

        await store.AddAsync(handoff, cancellationToken);
        Remember(handoff);
        await bus.PublishAsync(new HandoffRecorded(handoff), cancellationToken);
    }

    public async Task WaitsAsync(ResetWait wait, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref waits, known => known.SetItem(wait.Job, wait));

        if (MarksOf(wait.Job) is not [.., { Kind: RelayMarkKind.Waiting } last] || last.Connection != wait.Connection || last.Until != wait.ResumesAt)
        {
            await MarkAsync(new RelayMark(wait.Job, RelayMarkKind.Waiting, wait.Connection, wait.Since) { Until = wait.ResumesAt }, cancellationToken);
        }

        await bus.PublishAsync(new JobWaitsForReset(wait), cancellationToken);
    }

    public async Task ResumedAsync(JobResumedAtReset resumed, CancellationToken cancellationToken)
    {
        await MarkAsync(new RelayMark(resumed.Job, RelayMarkKind.Resumed, resumed.Connection, resumed.At), cancellationToken);
        await bus.PublishAsync(resumed, cancellationToken);
    }

    public void Ended(JobId job) => ImmutableInterlocked.Update(ref waits, known => known.Remove(job));

    private async Task<ModelChoice> DefaultsOfAsync(ConnectionName connection, CancellationToken cancellationToken) =>
        (await connections.CheckAsync(connection, cancellationToken)).Match(
            found => found.Capabilities.Get<OffersModels>().Match(
                offered => offered.DefaultChoice() with { Model = offered.DefaultModel.IsSome ? offered.DefaultModel : (offered.Models.Count > 0 ? Option<string>.Some(offered.Models[0]) : Option<string>.None) },
                () => ModelChoice.Default),
            _ => ModelChoice.Default);

    private void Remember(RelayMark mark) =>
        ImmutableInterlocked.Update(ref marks, known => Kept(known, mark.Job, mark, kept => (kept.Kind, kept.Connection, kept.At)));

    private void Remember(HandoffRecord handoff) =>
        ImmutableInterlocked.Update(ref handoffs, known => Kept(known, handoff.Job, handoff, kept => (kept.Attempt, kept.From, kept.To, kept.At)));

    private static ImmutableDictionary<JobId, ImmutableList<T>> Kept<T, TKey>(ImmutableDictionary<JobId, ImmutableList<T>> known, JobId job, T item, Func<T, TKey> key)
    {
        var list = known.GetValueOrDefault(job, []);

        return list.Exists(kept => EqualityComparer<TKey>.Default.Equals(key(kept), key(item))) ? known : known.SetItem(job, list.Add(item));
    }
}
