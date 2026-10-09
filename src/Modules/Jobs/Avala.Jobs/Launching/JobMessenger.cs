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
        if (job.State != JobState.Running || job.Session.IsNone)
        {
            return await agents.TellAsync(job, message, cancellationToken);
        }

        var notes = new List<string> { message };

        foreach (var briefing in briefings)
        {
            notes.AddRange((await briefing.BriefAsync(job.Id, cancellationToken)).Match<string[]>(note => [note], () => []));
        }

        return await agents.TellAsync(job, string.Join("\n\n", notes), cancellationToken);
    }
}
