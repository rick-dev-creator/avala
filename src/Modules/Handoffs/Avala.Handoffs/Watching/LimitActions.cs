using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Briefing;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Handoffs.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Handoffs.Watching;

internal sealed class LimitActions(IJobs jobs, BriefGatherer briefs, HandoffBook book, Compaction compaction)
{
    public const string ResetMessage = "The usage limit window has reset. Go on where you left off.";

    public async Task<bool> HandOffAsync(JobSituation situation, Decision decision, Option<string> pending, CancellationToken cancellationToken) =>
        await decision.Choice.Match(
            async choice =>
            {
                var handoff = await briefs.HandoffAsync(new BriefRequest(situation.History, situation.Connection, choice.Connection, decision.Why, pending), choice, cancellationToken);

                return (await jobs.HandOffAsync(situation.Job, handoff, cancellationToken)).IsSuccess;
            },
            () => Task.FromResult(false));

    public async Task<bool> ContinueAsync(JobSituation situation, Option<string> pending, CancellationToken cancellationToken)
    {
        var message = pending.Match(feedback => $"{ResetMessage}\n\n{feedback}", () => ResetMessage);

        foreach (var session in (await compaction.CompactableAsync(situation, cancellationToken)).Match<SessionId[]>(found => [found], () => []))
        {
            await book.MarkAsync(new RelayMark(situation.Job, RelayMarkKind.Compacting, situation.Connection, situation.Situation.Now), cancellationToken);
            _ = await compaction.CompactAsync(situation, session, cancellationToken);
        }

        if (!(await jobs.ContinueAsync(situation.Job, new JobMessage(message) { Author = MessageAuthor.Avala, CausedBy = MessageAuthor.Avala }, cancellationToken)).TryGetValue(out var continued, out _))
        {
            return false;
        }

        await book.ResumedAsync(new JobResumedAtReset(situation.Job, situation.Connection, continued.Conversation, situation.Situation.Now), cancellationToken);

        return true;
    }

    public Task WaitsAsync(ResetWait wait, CancellationToken cancellationToken) => book.WaitsAsync(wait, cancellationToken);

    public Option<ResetWait> WaitOf(JobId job) => book.WaitOf(job);

    public void Ended(JobId job) => book.Ended(job);
}
