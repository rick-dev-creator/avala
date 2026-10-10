namespace Avala.Jobs.Tests.Jobs;

internal enum Operation
{
    Submit,
    Start,
    Recover,
    Recheck,
    CompleteTurn,
    Pass,
    Retry,
    RetryInNewSession,
    RequestHelp,
    Hint,
    HintInNewSession,
    ContinueOnAnotherConnection,
    SendBack,
    SendBackInNewSession,
    Approve,
    Discard,
    Fail,
    Hold,
    HoldOverBudget,
    HandOff,
    Reopen,
    ReopenInNewSession,
}
