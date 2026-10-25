namespace Avala.Server.Activity.Domain;

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
