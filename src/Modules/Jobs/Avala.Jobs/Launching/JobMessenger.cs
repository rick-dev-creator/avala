using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;

namespace Avala.Jobs.Launching;

internal sealed class JobMessenger(IAgents agents, IEnumerable<IJobBriefing> briefings)
{
    public async Task<Result<AgentTurn, AgentError>> TellAsync(Job job, string message, CancellationToken cancellationToken)
    {
        if (job.State != JobState.Running || !job.Session.Match(agents.IsOpen, () => false))
        {
            return await agents.TellAsync(job, message, cancellationToken);
        }

        var briefs = new List<JobBrief>();

        foreach (var briefing in briefings)
        {
            briefs.AddRange((await briefing.BriefAsync(job.Id, cancellationToken)).Match<JobBrief[]>(brief => [brief], () => []));
        }

        var told = await agents.TellAsync(job, string.Join("\n\n", [message, .. briefs.Select(brief => brief.Text)]), cancellationToken);

        if (told.IsSuccess)
        {
            foreach (var brief in briefs)
            {
                await brief.Delivered(cancellationToken);
            }
        }

        return told;
    }
}
