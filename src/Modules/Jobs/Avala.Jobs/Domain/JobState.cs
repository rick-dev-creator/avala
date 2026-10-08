namespace Avala.Jobs.Domain;

internal enum JobState
{
    Open,
    Active,
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
