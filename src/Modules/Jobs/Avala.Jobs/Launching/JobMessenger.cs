using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Microsoft.Extensions.Logging;

namespace Avala.Jobs.Launching;

internal sealed partial class JobMessenger(IAgents agents, IEnumerable<IJobBriefing> briefings, ILogger<JobMessenger> logger)
{
    public Task<Result<AgentTurn, AgentError>> TellAsync(Job job, string message, MessageAuthor author, CancellationToken cancellationToken) =>
        TellAsync(job, new Told(message, author), cancellationToken);

    public async Task<Result<AgentTurn, AgentError>> TellAsync(Job job, Told told, CancellationToken cancellationToken)
    {
        if (job.State != JobState.Running || !job.Session.Match(agents.IsOpen, () => false))
        {
            return await agents.TellAsync(job, told, cancellationToken);
        }

        var briefs = new List<JobBrief>();

        try
        {
            foreach (var briefing in briefings)
            {
                briefs.AddRange((await briefing.BriefAsync(job.Id, cancellationToken)).Match<JobBrief[]>(brief => !brief.Reminder || Reminded(job, told) ? [brief] : [], () => []));
            }
        }
        catch
        {
            await SettleAsync(job, briefs, brief => brief.Undelivered);
            throw;
        }

        Result<AgentTurn, AgentError> sent;

        try
        {
            sent = await agents.TellAsync(job, told with { Text = string.Join("\n\n", [told.Text, .. briefs.Select(brief => brief.Text)]) }, cancellationToken);
        }
        catch
        {
            await SettleAsync(job, briefs, brief => brief.Undelivered);
            throw;
        }

        await SettleAsync(job, briefs, brief => sent.IsSuccess ? brief.Delivered : brief.Undelivered);

        return sent;
    }

    private static bool Reminded(Job job, Told told) =>
        told.Author == MessageAuthor.Person
        || told.Fresh
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

internal sealed record Told(string Text, MessageAuthor Author)
{
    public bool Fresh { get; init; }

    public IReadOnlyList<MessageId> Messages { get; init; } = [];
}
