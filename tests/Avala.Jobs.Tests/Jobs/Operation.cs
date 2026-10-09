namespace Avala.Jobs.Tests.Jobs;

internal enum Operation
{
    Submit,
    Start,
    Recover,
    CompleteTurn,
    Pass,
    Retry,
    RequestHelp,
    Hint,
    HintInNewSession,
    SendBack,
    Approve,
    Discard,
    Fail,
    Hold,
}
