using Avala.Jobs.Contracts;

namespace Avala.Workbench.Steering;

internal static class Steerability
{
    extension(JobStatus status)
    {
        public bool AcceptsMessages => status is JobStatus.NeedsHelp or JobStatus.AwaitingReview;

        public bool IsWorking => status is JobStatus.Draft or JobStatus.Preparing or JobStatus.Running or JobStatus.Checking;

        public bool IsOver => status is JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed;

        public bool CanBeInterrupted => status == JobStatus.Running;

        public bool CanBeStopped => status == JobStatus.Running;

        public bool CanBeReviewed => status is JobStatus.AwaitingReview or JobStatus.NeedsHelp;

        public bool CanBeDiscarded => !status.IsOver;
    }
}
