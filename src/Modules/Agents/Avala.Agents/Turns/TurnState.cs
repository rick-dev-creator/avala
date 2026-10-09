namespace Avala.Agents.Turns;

internal enum TurnState
{
    Live,
    Working,
    AwaitingPermission,
    AwaitingAnswer,
    Finished,
    Interrupted,
    Failed,
}
