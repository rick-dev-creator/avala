using Avala.Jobs.Contracts;

namespace Avala.Workbench.Steering;

internal static class Steerability
{
    extension(JobStatus status)
    {
        public bool AcceptsMessages => status is JobStatus.NeedsHelp or JobStatus.AwaitingReview;

        public bool CanBeInterrupted => status == JobStatus.Running;

        public bool CanBeStopped => status == JobStatus.Running;

        public bool CanBeReviewed => status is JobStatus.AwaitingReview or JobStatus.NeedsHelp;

        public bool CanBeDiscarded => status is not (JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed);
    }
}
