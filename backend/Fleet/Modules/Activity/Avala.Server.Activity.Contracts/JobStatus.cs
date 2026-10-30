namespace Avala.Server.Activity.Contracts;

internal enum JobStatus
{
    Draft,
    Preparing,
    Running,
    Checking,
    NeedsHelp,
    AwaitingReview,
    Approved,
    Discarded,
    Failed,
}
