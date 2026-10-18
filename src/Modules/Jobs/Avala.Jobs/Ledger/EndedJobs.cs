using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;

namespace Avala.Jobs.Ledger;

internal sealed class EndedJobs(JobQueues queues) : IEndedJobs
{
    public async ValueTask<bool> WhileEndedAsync(JobId job, Func<CancellationToken, Task> work, CancellationToken cancellationToken) =>
        await (await queues.RunAsync(
            job,
            async (found, token) =>
            {
                if (found.State is not (JobState.Approved or JobState.Discarded or JobState.Failed))
                {
                    return false;
                }

                await work(token);

                return true;
            },
            cancellationToken)).Match(
            ended => Task.FromResult(ended),
            async () =>
            {
                await work(cancellationToken);

                return true;
            });
}
