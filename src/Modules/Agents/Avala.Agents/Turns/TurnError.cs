namespace Avala.Agents.Turns;

internal enum TurnError
{
    ForeignEvent,
    TurnEnded,
    UnexpectedTurnStart,
    ItemAlreadyStarted,
    ItemAlreadyCompleted,
    UnknownItem,
    ItemAlreadyBackgrounded,
    PermissionAlreadyPending,
    NoPendingPermission,
    MalformedForm,
    FormAlreadyPending,
    NoPendingForm,
    NoPendingRequest,
    NoPendingCall,
}
