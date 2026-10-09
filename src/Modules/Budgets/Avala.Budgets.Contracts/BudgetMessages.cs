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
}

public sealed record BudgetCaps(IReadOnlyList<Cost> CostPerJob, Option<long> TokensPerJob, Option<double> HoldAtLimit);

public sealed record SessionBudget(SessionId Session, BudgetFileStatus File, Option<BudgetError> Error, BudgetCaps Caps, Option<FileOrigin> Origin);

public sealed record BudgetBreach(BudgetMeasure Measure, string Subject, decimal Measured, decimal Cap, Option<BudgetError> Error);

public sealed record BudgetIntervention(JobHold Hold, BudgetBreach Breach, DateTimeOffset At);

public sealed record BudgetLoaded(SessionBudget Budget) : IIntegrationEvent;

public sealed record BudgetIntervened(BudgetIntervention Intervention) : IIntegrationEvent;
