using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;

namespace Avala.Jobs.Ledger;

internal static class JobStatuses
{
    extension(JobState state)
    {
        public JobStatus Status => state switch
        {
            JobState.Draft => JobStatus.Draft,
            JobState.Preparing => JobStatus.Preparing,
            JobState.Running => JobStatus.Running,
            JobState.Checking => JobStatus.Checking,
            JobState.NeedsHelp => JobStatus.NeedsHelp,
            JobState.AwaitingReview => JobStatus.AwaitingReview,
            JobState.Approved => JobStatus.Approved,
            JobState.Discarded => JobStatus.Discarded,
            JobState.Failed => JobStatus.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "A superstate is never a job's state"),
        };
    }
}
