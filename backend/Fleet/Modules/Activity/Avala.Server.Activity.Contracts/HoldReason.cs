namespace Avala.Server.Activity.Contracts;

internal enum HoldReason
{
    Stalled,
    SessionLost,
    BudgetExceeded,
    LimitNearlyReached,
    InvalidBudget,
    MemoryExceeded,
    Interrupted,
    Stopped,
    NotResumable,
    InvalidChecks,
    CouldNotStart,
    TurnFailed,
}
