namespace Avala.Jobs.Domain;

internal enum JobTrigger
{
    Submit,
    Start,
    Recover,
    CompleteTurn,
    Pass,
    Retry,
    RequestHelp,
    Hint,
    SendBack,
    Approve,
    Discard,
    Fail,
}
