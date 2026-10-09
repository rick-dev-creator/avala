using Avala.Jobs.Contracts;
using Avala.Jobs.Holding;
using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Sdk;

namespace Avala.Jobs.Recovery;

internal sealed class ResumeJob(DeferredJobs deferred, JobLauncher launcher, HoldJob hold)
{
    public async Task<Result<JobContinuation, JobRejection>> ExecuteAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.State != JobState.Running || !deferred.Release(job.Id))
        {
            return JobRejection.NotDeferred;
        }

        var resumed = await launcher.ResumeAsync(job, cancellationToken);

        if (resumed.IsFailure)
        {
            _ = await hold.ExecuteAsync(job, HoldReason.NotResumable, cancellationToken);
        }

        return resumed;
    }
}
