namespace Avala.Jobs.Domain;

internal enum AttemptOutcome
{
    Running,
    AwaitingCheck,
    Passed,
    Rejected,
    Interrupted,
}
