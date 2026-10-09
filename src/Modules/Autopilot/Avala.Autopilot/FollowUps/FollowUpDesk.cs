using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Sourcing;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Autopilot.FollowUps;

internal sealed class FollowUpDesk(FollowUpPolicy policy, ITaskLedger ledger, IAgents agents, IEventBus bus)
    : IHandle<SessionOpened>, IHandle<JobSessionStarted>, IHandle<AgentActivity>
{
    private readonly Dictionary<SessionId, string> worktrees = [];
    private readonly Dictionary<SessionId, JobId> jobs = [];

    public ValueTask HandleAsync(SessionOpened integrationEvent, CancellationToken cancellationToken)
    {
        worktrees[integrationEvent.Session] = integrationEvent.WorkingDirectory;

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        jobs[integrationEvent.Session] = integrationEvent.Job;

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Event is not ToolCalled { Tool: FollowUpTool.Name } called)
        {
            return;
        }

        var proposing = new ProposingSession(
            called.Session,
            jobs.TryGetValue(called.Session, out var job) ? job : Option<JobId>.None,
            worktrees.TryGetValue(called.Session, out var worktree) ? worktree : Option<string>.None);
        var decision = await policy.DecideAsync(proposing, called.Input, cancellationToken);
        await decision.Task.Match(task => ledger.ProposeAsync(task, decision.At, cancellationToken), () => Task.CompletedTask);
        _ = await agents.ReturnAsync(called.Session, Result(called.Item, decision), cancellationToken);
        await bus.PublishAsync(new FollowUpDecided(decision), cancellationToken);
    }

    private static ToolResult Result(ItemId item, FollowUpDecision decision) =>
        decision.Task.Match(
            task => new ToolResult(item, $"Accepted: the follow-up is queued as {task.Key} and runs as a job of its own after this one."),
            () => new ToolResult(item, $"Refused: {Reason(decision.Refusal.Match(refusal => refusal, () => FollowUpRefusal.NotAllowed))}")
            {
                IsError = decision.Refusal == Option<FollowUpRefusal>.Some(FollowUpRefusal.MalformedInput),
            });

    private static string Reason(FollowUpRefusal refusal) => refusal switch
    {
        FollowUpRefusal.MalformedInput => "the input needs an instruction as non-empty text.",
        FollowUpRefusal.NoJob => "this session runs no job of a repository.",
        FollowUpRefusal.NotAutonomous => "follow-ups are accepted only from jobs that run autonomous.",
        FollowUpRefusal.UnreadableRules => "the repository's .avala/jobs.json cannot be read from the job's base commit.",
        _ => "the repository's .avala/jobs.json does not accept follow-ups.",
    };
}
