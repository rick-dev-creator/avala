namespace Avala.Agents.Domain;

internal enum TurnState
{
    Live,
    Working,
    AwaitingPermission,
    Finished,
    Interrupted,
    Failed,
}
