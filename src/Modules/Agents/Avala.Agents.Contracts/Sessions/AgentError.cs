namespace Avala.Agents.Contracts.Sessions;

public enum AgentError
{
    ProviderUnavailable,
    SessionClosed,
    TurnInProgress,
    NoTurnInProgress,
    NoPendingPermission,
    Unsupported,
    CannotResume,
    NoPendingForm,
    InvalidAnswer,
}
