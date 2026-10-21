using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk.Events;
using Avala.Transcripts.Contracts;

namespace Avala.Delegation.Listing;

internal sealed class SubagentDesk(ITranscripts transcripts, ChildRoster roster, IAgents agents)
    : IHandle<JobSessionStarted>, IHandle<AgentActivity>, IHandle<SessionEnded>
{
    private readonly Dictionary<SessionId, JobId> jobs = [];
    private readonly Dictionary<JobId, SubagentActivity> live = [];
    private readonly Dictionary<JobId, IReadOnlyList<InProcessSubagent>> earlier = [];

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        jobs[integrationEvent.Session] = integrationEvent.Job;

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(SessionEnded integrationEvent, CancellationToken cancellationToken)
    {
        if (jobs.TryGetValue(integrationEvent.Session, out var job))
        {
            live[job] = Of(job).Ended(integrationEvent.Session);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Event is ToolCalled { Tool: SubagentsTool.Name } called)
        {
            _ = await agents.ReturnAsync(called.Session, await AnswerAsync(called, cancellationToken), cancellationToken);

            return;
        }

        if (jobs.TryGetValue(integrationEvent.Event.Session, out var job))
        {
            live[job] = Of(job).Apply(integrationEvent.Event);
        }
    }

    private async Task<ToolResult> AnswerAsync(ToolCalled called, CancellationToken cancellationToken)
    {
        if (!SubagentsTool.Accepts(called.Input))
        {
            return SubagentAnswers.Refused(called.Item, ListingRefusal.MalformedInput);
        }

        if (!jobs.TryGetValue(called.Session, out var job))
        {
            return SubagentAnswers.Refused(called.Item, ListingRefusal.NoJob);
        }

        var activity = Of(job);
        IReadOnlyList<ListedSubagent> inProcess =
        [
            .. (await EarlierAsync(job, cancellationToken)).Select(agent => Listed(agent, Asking.Nothing)),
            .. activity.Subagents.Select(agent => Listed(agent, activity.AskingOf(agent))),
        ];
        var crossHarness = await roster.OfAsync(job, child => Of(child).Asking, cancellationToken);

        return SubagentAnswers.Listed(called.Item, [.. inProcess, .. crossHarness]);
    }

    private async Task<IReadOnlyList<InProcessSubagent>> EarlierAsync(JobId job, CancellationToken cancellationToken)
    {
        if (!earlier.TryGetValue(job, out var known))
        {
            known =
            [
                .. (await transcripts.EarlierRunsAsync(job, cancellationToken))
                    .GroupBy(fact => fact.Run)
                    .SelectMany(run => run.Select(fact => fact.Fact).OfType<AgentActed>().Aggregate(SubagentActivity.None, (folded, acted) => folded.Apply(acted.Event)).Ended().Subagents),
            ];
            earlier[job] = known;
        }

        return known;
    }

    private SubagentActivity Of(JobId job) => live.TryGetValue(job, out var activity) ? activity : SubagentActivity.None;

    private static ListedSubagent Listed(InProcessSubagent agent, Asking asking)
    {
        var state = Lifecycle.Of(agent.Outcome, asking);

        return new ListedSubagent(agent.Name, SubagentKind.InProcess, state, Lifecycle.Status(state, asking, Avala.Sdk.Option<HoldReason>.None));
    }
}
