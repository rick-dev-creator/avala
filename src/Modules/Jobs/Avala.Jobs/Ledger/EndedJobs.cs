using Avala.Agents.Contracts;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;

namespace Avala.Jobs.Ledger;

internal sealed class EndedJobs(JobQueues queues, IAgents agents) : IEndedJobs
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

    public async ValueTask<bool> WhileRestingAsync(JobId job, Func<CancellationToken, Task> work, CancellationToken cancellationToken) =>
        (await queues.RunAsync(
            job,
            async (found, token) =>
            {
                if (!Resting(found))
                {
                    return false;
                }

                await work(token);

                return true;
            },
            cancellationToken)).Match(resting => resting, () => false);

    private bool Resting(Job job) =>
        job.State is JobState.AwaitingReview or JobState.NeedsHelp
        && job.Held != Option<HoldReason>.Some(HoldReason.CouldNotStart)
        && job.Session.Match(session => !agents.IsOpen(session), () => true);
}
