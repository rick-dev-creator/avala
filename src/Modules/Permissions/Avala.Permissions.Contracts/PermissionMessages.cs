using System.Text.Json.Serialization;
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
    UnknownAutonomy,
    UnknownStrategy,
    NotAwaitingAnswer,
    NotARepositoryRule,
    RepositoryUnwritable,
    BeyondParent,
    InvalidAnswer,
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
    Withdrawn,
    LeftToParent,
}

public enum PassReason
{
    PassedByParent,
    BeyondParent,
    ParentTimedOut,
    ParentUnreachable,
}

public sealed record SessionPolicy(
    SessionId Session,
    PolicyFileStatus File,
    Option<PolicyError> Error,
    IReadOnlyList<PolicyRule> Rules,
    Option<FileOrigin> Origin)
{
    public Autonomy Autonomy { get; init; }

    public FormStrategy Strategy { get; init; }
}

public sealed record RepositoryPolicy(
    PolicyFileStatus File,
    Option<PolicyError> Error,
    IReadOnlyList<PolicyRule> Rules,
    Option<FileOrigin> Origin)
{
    public Autonomy Autonomy { get; init; }

    public FormStrategy Strategy { get; init; }
}

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
    DateTimeOffset At)
{
    public Autonomy Autonomy { get; init; }

    public Option<PolicyRule> RepositoryRule { get; init; }

    public Option<JobId> Parent { get; init; }

    public Option<PassReason> Passed { get; init; }
}

public sealed record SessionAutonomy(SessionId Session, JobId Job, Autonomy Declared, Option<Autonomy> Requested, Autonomy Effective, bool Refused)
{
    public bool ReadOnly { get; init; }
}

public enum AssumptionBasis
{
    RecommendedOption,
    FirstOption,
    AgentJudgment,
    Confirmed,
}

public sealed record Assumption(string Field, string Prompt, AssumptionBasis Basis, IReadOnlyList<string> Chosen);

public sealed record FormDecision(
    SessionId Session,
    TurnId Turn,
    ItemId Item,
    Option<JobId> Job,
    AgentForm Form,
    Autonomy Autonomy,
    Option<FormAnswer> Answer,
    IReadOnlyList<Assumption> Assumptions,
    DecisionDelivery Delivery,
    DateTimeOffset At)
{
    public Option<JobId> Parent { get; init; }

    public Option<PassReason> Passed { get; init; }
}

public enum Remember
{
    Once,
    ForThisJob,
    InThisRepository,
}

public sealed record PermissionReply(ItemId Item, PermissionAnswer Answer)
{
    public Option<string> Message { get; init; }

    public Remember Remember { get; init; }
}

public sealed record HumanAnswer(
    SessionId Session,
    Option<JobId> Job,
    ItemId Item,
    ItemKind Kind,
    string Target,
    PermissionAnswer Answer,
    Option<string> Message,
    [property: JsonPropertyName("SessionRule")] Option<PolicyRule> Rule,
    DateTimeOffset At)
{
    public Option<PolicyRule> RepositoryRule { get; init; }

    public Option<PolicyError> RepositoryError { get; init; }

    public Option<JobId> Parent { get; init; }
}

public sealed record PolicyLoaded(SessionPolicy Policy) : IIntegrationEvent;

public sealed record PermissionDecided(PolicyDecision Decision) : IIntegrationEvent;

public sealed record AutonomyApplied(SessionAutonomy Autonomy) : IIntegrationEvent;

public sealed record FormDecided(FormDecision Decision) : IIntegrationEvent;

public sealed record PermissionAnswered(HumanAnswer Answer) : IIntegrationEvent;
