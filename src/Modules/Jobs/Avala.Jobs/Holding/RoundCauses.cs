using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Ledger;
using Avala.Sdk;

namespace Avala.Jobs.Holding;

internal sealed class RoundCauses(JobLedger ledger, JobQueues queues, HoldJob hold) : IJobRounds
{
    public async ValueTask PersonJoinedAsync(SessionId session, CancellationToken cancellationToken)
    {
        var joining = await ledger.JobOfSessionAsync(session, cancellationToken);

        while (joining.IsSome)
        {
            joining = await joining.Match(job => JoinAsync(job, cancellationToken), () => Task.FromResult(Option<JobId>.None));
        }
    }

    private async Task<Option<JobId>> JoinAsync(JobId job, CancellationToken cancellationToken) =>
        (await queues.RunAsync(
            job,
            async (found, token) =>
            {
                await hold.JoinedAsync(found, MessageAuthor.Person, token);

                return found.Parent;
            },
            cancellationToken)).Bind(parent => parent);
}
