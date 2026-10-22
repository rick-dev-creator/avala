using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Microsoft.Extensions.Logging;

namespace Avala.Jobs.Launching;

internal sealed partial class JobMessenger(IAgents agents, IEnumerable<IJobBriefing> briefings, ILogger<JobMessenger> logger)
{
    public async Task<Result<AgentTurn, AgentError>> TellAsync(Job job, string message, MessageAuthor author, CancellationToken cancellationToken)
    {
        if (!Briefed(job, author) || job.State != JobState.Running || !job.Session.Match(agents.IsOpen, () => false))
        {
            return await agents.TellAsync(job, message, author, cancellationToken);
        }

        var briefs = new List<JobBrief>();

        try
        {
            foreach (var briefing in briefings)
            {
                briefs.AddRange((await briefing.BriefAsync(job.Id, cancellationToken)).Match<JobBrief[]>(brief => [brief], () => []));
            }
        }
        catch
        {
            await SettleAsync(job, briefs, brief => brief.Undelivered);
            throw;
        }

        Result<AgentTurn, AgentError> told;

        try
        {
            told = await agents.TellAsync(job, string.Join("\n\n", [message, .. briefs.Select(brief => brief.Text)]), author, cancellationToken);
        }
        catch
        {
            await SettleAsync(job, briefs, brief => brief.Undelivered);
            throw;
        }

        await SettleAsync(job, briefs, brief => told.IsSuccess ? brief.Delivered : brief.Undelivered);

        return told;
    }

    private static bool Briefed(Job job, MessageAuthor author) =>
        author == MessageAuthor.Person
        || (job.Attempts.Count > 0 && job.Attempts[^1].Origin is AttemptOrigin.Retry or AttemptOrigin.Recovery or AttemptOrigin.Handoff or AttemptOrigin.Reopen);

    private async Task SettleAsync(Job job, IReadOnlyList<JobBrief> briefs, Func<JobBrief, Func<CancellationToken, Task>> settle)
    {
        foreach (var brief in briefs)
        {
            try
            {
                await settle(brief)(CancellationToken.None);
            }
            catch (Exception exception)
            {
                LogUnsettled(job.Id.Value, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A briefing of job {Job} could not record whether its message reached the agent")]
    private partial void LogUnsettled(Guid job, Exception exception);
}
