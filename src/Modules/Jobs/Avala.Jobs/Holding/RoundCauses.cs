using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Ledger;

namespace Avala.Jobs.Holding;

internal sealed class RoundCauses(JobLedger ledger, JobQueues queues, HoldJob hold) : IJobRounds
{
    public async ValueTask PersonJoinedAsync(SessionId session, CancellationToken cancellationToken)
    {
        foreach (var job in (await ledger.JobOfSessionAsync(session, cancellationToken)).Match<JobId[]>(found => [found], () => []))
        {
            _ = await queues.RunAsync(
                job,
                async (found, token) =>
                {
                    await hold.JoinedAsync(found, MessageAuthor.Person, token);

                    return true;
                },
                cancellationToken);
        }
    }
}
