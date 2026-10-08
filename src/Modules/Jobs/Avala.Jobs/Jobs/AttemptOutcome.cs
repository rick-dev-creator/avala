namespace Avala.Jobs.Jobs;

internal enum AttemptOutcome
{
    Running,
    AwaitingCheck,
    Passed,
    Rejected,
    Interrupted,
}
