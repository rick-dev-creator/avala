namespace Avala.Jobs.Jobs;

internal enum AttemptOrigin
{
    Initial,
    Retry,
    Hint,
    SendBack,
    Recovery,
}
