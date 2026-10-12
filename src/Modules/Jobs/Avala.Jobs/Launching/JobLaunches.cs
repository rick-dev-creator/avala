using Avala.Jobs.Contracts;
using Avala.Jobs.Ledger;
using Avala.Sdk;

namespace Avala.Jobs.Launching;

internal sealed class JobLaunches(JobQueues queues, StartJob start) : IJobLaunches
{
    public async ValueTask<Result<JobId, JobRejection>> RetryAsync(JobId job, CancellationToken cancellationToken) =>
        (await queues.RunAsync(job, start.RetryAsync, cancellationToken)).Match(retried => retried, () => Result<JobId, JobRejection>.Failure(JobRejection.UnknownJob));
}
