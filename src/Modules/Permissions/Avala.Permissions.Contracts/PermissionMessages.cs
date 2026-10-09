using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.Contracts;

public enum PolicyError
{
    Unreadable,
    TooLarge,
    Malformed,
    UnknownField,
    UnknownKind,
    UnknownScope,
    MissingAnswer,
    UnknownAnswer,
    ScopeNeedsFileEdits,
}

public enum PolicyFileStatus
{
    Absent,
    Applied,
    Rejected,
}

public enum DecisionDelivery
{
    Answered,
    LeftToHuman,
    Undelivered,
}

public sealed record SessionPolicy(
    SessionId Session,
    PolicyFileStatus File,
    Option<PolicyError> Error,
    IReadOnlyList<PolicyRule> Rules,
    Option<FileOrigin> Origin);

public sealed record PolicyDecision(
    SessionId Session,
    TurnId Turn,
    ItemId Item,
    Option<JobId> Job,
    ItemKind Kind,
    string Target,
    PolicyAnswer Answer,
    Option<PolicyRule> Rule,
    DecisionDelivery Delivery,
    DateTimeOffset At);

public sealed record PolicyLoaded(SessionPolicy Policy) : IIntegrationEvent;

public sealed record PermissionDecided(PolicyDecision Decision) : IIntegrationEvent;
