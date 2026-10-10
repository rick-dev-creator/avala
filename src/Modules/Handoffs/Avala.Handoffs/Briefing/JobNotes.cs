using System.Collections.Immutable;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Transcripts.Contracts;

namespace Avala.Handoffs.Briefing;

internal sealed record Notes(IReadOnlyList<PlanStep> Plan, Option<string> LastMessage)
{
    public static Notes None { get; } = new([], Option<string>.None);

    public Option<ItemId> Speaking { get; init; }

    public Notes Apply(IAgentEvent activity) => activity switch
    {
        PlanUpdated plan => this with { Plan = plan.Steps },
        ItemStarted { Kind: ItemKind.Message } started => this with { Speaking = started.Item, LastMessage = string.Empty },
        ItemProgressed progressed when Speaking == Option<ItemId>.Some(progressed.Item) =>
            this with { LastMessage = LastMessage.Match(text => text + progressed.Text, () => progressed.Text) },
        _ => this,
    };
}

internal sealed class JobNotes(ITranscripts transcripts) : IHandle<JobSessionStarted>, IHandle<AgentActivity>
{
    private ImmutableDictionary<SessionId, JobId> sessions = ImmutableDictionary<SessionId, JobId>.Empty;
    private ImmutableDictionary<JobId, Notes> notes = ImmutableDictionary<JobId, Notes>.Empty;

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref sessions, known => known.SetItem(integrationEvent.Session, integrationEvent.Job));

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref sessions).TryGetValue(integrationEvent.Event.Session, out var job))
        {
            ImmutableInterlocked.Update(ref notes, known => known.SetItem(job, known.GetValueOrDefault(job, Notes.None).Apply(integrationEvent.Event)));
        }

        return ValueTask.CompletedTask;
    }

    public async Task<Notes> OfAsync(JobId job, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref notes).TryGetValue(job, out var kept))
        {
            return kept;
        }

        return (await transcripts.EarlierRunsAsync(job, cancellationToken))
            .Select(fact => fact.Fact)
            .OfType<AgentActed>()
            .Aggregate(Notes.None, (folded, acted) => folded.Apply(acted.Event));
    }
}
