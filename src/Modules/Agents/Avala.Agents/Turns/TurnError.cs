namespace Avala.Agents.Turns;

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
    MalformedForm,
    FormAlreadyPending,
    NoPendingForm,
}
