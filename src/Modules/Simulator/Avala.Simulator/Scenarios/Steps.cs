using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Simulator.Scenarios;

internal interface IStep;

internal sealed record Say(ItemId Item, ItemKind Kind, string Title, IReadOnlyList<string> Chunks) : IStep;

internal sealed record Draw(ItemId Item, string Title, string MediaType, IReadOnlyList<string> Chunks) : IStep;

internal sealed record WriteFile(ItemId Item, string Path, string Content) : IStep;

internal sealed record RunCommand(ItemId Item, string Command, string Output, bool AsksPermission) : IStep;

internal sealed record WithdrawnPermission(RunCommand Withdrawn, RunCommand Kept) : IStep;

internal sealed record WriteThroughCommand(ItemId Item, string Command, string Path, string Content) : IStep;

internal sealed record UseTool(ItemId Item, ItemKind Kind, string Title, string Target, string Input, string Output, bool AsksPermission) : IStep;

internal sealed record Spawn(ItemId Item, string Command, Workload Workload) : IStep;

internal enum Workload
{
    Build,
    Server,
}

internal sealed record Ask(ItemId Item, AgentForm Form) : IStep;

internal sealed record CallTool(ItemId Item, string Tool, string Input) : IStep;

internal sealed record CallTools(IReadOnlyList<CallTool> Calls) : IStep
{
    public Option<string> AnswersChildren { get; init; }
}

internal sealed record UpdatePlan(IReadOnlyList<PlanStep> Steps) : IStep;

internal sealed record ReportUsage(TokenUsage Tokens, Cost Cost) : IStep;

internal sealed record ReportLimit(UsageLimit Limit) : IStep;

internal sealed record ReportLimitResetting(string Window, double Used, TimeSpan ResetsIn) : IStep;

internal sealed record Open(ItemId Item, ItemKind Kind, string Title) : IStep;

internal sealed record Crash(string Reason) : IStep;

internal sealed record Finish : IStep;

internal sealed record Emit(TimeSpan Gap, IAgentEvent Event) : IStep;

internal sealed record AwaitPermission(PermissionDecision Decision) : IStep;

internal sealed record AwaitAnswer(FormAnswer Answer) : IStep;

internal sealed record AwaitReturn(ToolResult Result) : IStep;

internal sealed record AwaitInterrupt : IStep;

internal sealed record AwaitMessage : IStep;

internal sealed record PutFile(TimeSpan Gap, string Path, string Content) : IStep;

internal sealed record Hangup : IStep;

internal sealed record Recall(ItemId Item) : IStep;

internal sealed record UnlessTold(string Fragment, IStep Step) : IStep;

internal sealed record Diverge(string Reason) : IStep;
