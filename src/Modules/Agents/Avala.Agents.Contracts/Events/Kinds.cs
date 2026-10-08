namespace Avala.Agents.Contracts.Events;

public enum ItemKind
{
    Message,
    Reasoning,
    FileEdit,
    Command,
    Search,
    Web,
    Mcp,
    Subagent,
    Other,
}

public enum ItemOutcome
{
    Succeeded,
    Failed,
    Cancelled,
    Abandoned,
    Expired,
}

public enum TurnOutcome
{
    Finished,
    Interrupted,
    Failed,
}

public enum PermissionAnswer
{
    Allow,
    Deny,
}

public enum PlanStepStatus
{
    Pending,
    InProgress,
    Done,
}
