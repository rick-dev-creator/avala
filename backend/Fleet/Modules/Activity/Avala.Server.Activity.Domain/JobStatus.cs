namespace Avala.Server.Activity.Domain;

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
