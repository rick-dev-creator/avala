namespace Avala.Agents.Turns;

internal enum TurnState
{
    Live,
    Working,
    AwaitingPermission,
    Finished,
    Interrupted,
    Failed,
}
