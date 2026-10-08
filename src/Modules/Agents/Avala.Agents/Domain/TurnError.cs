namespace Avala.Agents.Domain;

internal enum TurnError
{
    ForeignEvent,
    TurnEnded,
    UnexpectedTurnStart,
    ItemAlreadyStarted,
    ItemAlreadyCompleted,
    UnknownItem,
    PermissionAlreadyPending,
    NoPendingPermission,
}
