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
        await bus.PublishAsync(new JobWaitsForReset(wait), cancellationToken);
    }

    public void Ended(JobId job) => ImmutableInterlocked.Update(ref waits, known => known.Remove(job));

    private async Task<ModelChoice> DefaultsOfAsync(ConnectionName connection, CancellationToken cancellationToken) =>
        (await connections.CheckAsync(connection, cancellationToken)).Match(
            found => found.Capabilities.Get<OffersModels>().Match(
                offered => offered.DefaultChoice() with { Model = offered.DefaultModel.IsSome ? offered.DefaultModel : (offered.Models.Count > 0 ? Option<string>.Some(offered.Models[0]) : Option<string>.None) },
                () => ModelChoice.Default),
            _ => ModelChoice.Default);

    private void Remember(HandoffRecord handoff) =>
        ImmutableInterlocked.Update(ref handoffs, known => known.SetItem(handoff.Job, known.GetValueOrDefault(handoff.Job, []).Add(handoff)));
}
