namespace Avala.Jobs.Domain;

internal enum AttemptOrigin
{
    Initial,
    Retry,
    Hint,
    SendBack,
    Recovery,
}
