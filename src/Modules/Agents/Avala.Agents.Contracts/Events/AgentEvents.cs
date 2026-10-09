using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Contracts.Events;

public sealed record TurnStarted(SessionId Session, TurnId Turn) : IAgentEvent;

public sealed record ItemStarted(SessionId Session, TurnId Turn, ItemId Item, ItemKind Kind, string Title) : IAgentEvent;

public sealed record CanvasStarted(SessionId Session, TurnId Turn, ItemId Item, string Title, string MediaType) : IAgentEvent;

public sealed record ItemProgressed(SessionId Session, TurnId Turn, ItemId Item, string Text) : IAgentEvent;

public sealed record ItemCompleted(SessionId Session, TurnId Turn, ItemId Item, ItemOutcome Outcome) : IAgentEvent;

public sealed record PermissionRequested(SessionId Session, TurnId Turn, ItemId Item, string Title, ItemKind Kind, string Target) : IAgentEvent;

public sealed record PermissionResolved(SessionId Session, TurnId Turn, ItemId Item, PermissionAnswer Answer) : IAgentEvent;

public sealed record FormRequested(SessionId Session, TurnId Turn, ItemId Item, AgentForm Form) : IAgentEvent;

public sealed record FormAnswered(SessionId Session, TurnId Turn, ItemId Item, FormAnswer Answer) : IAgentEvent;

public sealed record ToolCalled(SessionId Session, TurnId Turn, ItemId Item, string Tool, string Input) : IAgentEvent;

public sealed record ToolReturned(SessionId Session, TurnId Turn, ItemId Item, ToolResult Result) : IAgentEvent;

public sealed record MessageQueued(SessionId Session, TurnId Turn, string Text) : IAgentEvent;

public sealed record PlanUpdated(SessionId Session, TurnId Turn, IReadOnlyList<PlanStep> Steps) : IAgentEvent;

public sealed record UsageReported(SessionId Session, TurnId Turn, TokenUsage Tokens, Option<Cost> Cost) : IAgentEvent;

public sealed record LimitReported(SessionId Session, TurnId Turn, UsageLimit Limit) : IAgentEvent;

public sealed record ResumeTokenIssued(SessionId Session, TurnId Turn, ResumeToken Token) : IAgentEvent;

public sealed record TurnCompleted(SessionId Session, TurnId Turn, TurnOutcome Outcome) : IAgentEvent;
