using Avala.Handoffs.Briefing;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Handoffs.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Handoffs.Watching;

internal sealed class LimitActions(IJobs jobs, BriefGatherer briefs, HandoffBook book, IEventBus bus)
{
    public const string ResetMessage = "The usage limit window has reset. Go on where you left off.";

    public async Task<bool> HandOffAsync(JobSituation situation, Decision decision, Option<string> pending, CancellationToken cancellationToken) =>
        await decision.Choice.Match(
            async choice =>
            {
                var brief = await briefs.WriteAsync(new BriefRequest(situation.History, situation.Connection, choice.Connection, decision.Why, pending), cancellationToken);

                return (await jobs.HandOffAsync(situation.Job, new JobHandoff(choice, brief), cancellationToken)).IsSuccess;
            },
            () => Task.FromResult(false));

    public async Task<bool> ContinueAsync(JobSituation situation, Option<string> pending, CancellationToken cancellationToken)
    {
        var message = pending.Match(feedback => $"{ResetMessage}\n\n{feedback}", () => ResetMessage);

        if (!(await jobs.ContinueAsync(situation.Job, message, cancellationToken)).TryGetValue(out var continued, out _))
        {
            return false;
        }

        await bus.PublishAsync(new JobResumedAtReset(situation.Job, situation.Connection, continued.Conversation, situation.Situation.Now), cancellationToken);

        return true;
    }

    public Task WaitsAsync(ResetWait wait, CancellationToken cancellationToken) => book.WaitsAsync(wait, cancellationToken);

    public void Ended(JobId job) => book.Ended(job);
}
