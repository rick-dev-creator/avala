using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workspaces.Contracts;

namespace Avala.Budgets.Contracts;

public enum BudgetError
{
    Unreadable,
    TooLarge,
    Malformed,
    UnknownField,
    InvalidCost,
    InvalidTokens,
    InvalidThreshold,
    InvalidMemory,
    InvalidRunningJobs,
    InvalidCarve,
}

public enum BudgetFileStatus
{
    Absent,
    Applied,
    Rejected,
}

public enum BudgetMeasure
{
    Cost,
    Tokens,
    Limit,
    Declaration,
    Memory,
}

public sealed record BudgetCaps(IReadOnlyList<Cost> CostPerJob, Option<long> TokensPerJob, Option<double> HoldAtLimit)
{
    public Option<long> MemoryPerJobMegabytes { get; init; }

    public Option<double> CarvePerChild { get; init; }
}

public sealed record BudgetCarve(JobId Parent, JobId Child, IReadOnlyList<Cost> Cost, Option<long> Tokens, double Share, DateTimeOffset At);

public sealed record BudgetCarved(BudgetCarve Carve) : IIntegrationEvent;

public sealed record MachineBudget(Option<int> RunningJobs, BudgetFileStatus File, Option<BudgetError> Error);

public sealed record JobQueued(JobId Job, int Running, int Limit) : IIntegrationEvent;

public sealed record JobAdmitted(JobId Job) : IIntegrationEvent;

public sealed record SessionBudget(SessionId Session, BudgetFileStatus File, Option<BudgetError> Error, BudgetCaps Caps, Option<FileOrigin> Origin);

public sealed record ConnectionCaps(ConnectionName Connection, BudgetCaps Caps);

public sealed record RepositoryBudget(
    BudgetFileStatus File,
    Option<BudgetError> Error,
    BudgetCaps Caps,
    IReadOnlyList<ConnectionCaps> Connections,
    Option<FileOrigin> Origin);

public sealed record BudgetBreach(BudgetMeasure Measure, string Subject, decimal Measured, decimal Cap, Option<BudgetError> Error);

public sealed record BudgetIntervention(JobHold Hold, BudgetBreach Breach, DateTimeOffset At);

public sealed record BudgetLoaded(SessionBudget Budget) : IIntegrationEvent;

public sealed record BudgetIntervened(BudgetIntervention Intervention) : IIntegrationEvent;
