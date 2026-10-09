using Avala.Jobs.Contracts;

namespace Avala.Workbench.Steering;

internal static class Steerability
{
    extension(JobStatus status)
    {
        public bool AcceptsMessages => status is JobStatus.NeedsHelp or JobStatus.AwaitingReview;

        public bool CanBeInterrupted => status == JobStatus.Running;

        public bool CanBeStopped => status is not (JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed);
    }
}
