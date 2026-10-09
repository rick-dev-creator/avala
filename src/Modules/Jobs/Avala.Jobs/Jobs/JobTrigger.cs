namespace Avala.Jobs.Jobs;

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
    Hold,
}
