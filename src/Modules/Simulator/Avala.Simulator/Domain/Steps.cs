using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Simulator.Domain;

internal interface IStep;

internal sealed record Say(ItemId Item, ItemKind Kind, string Title, IReadOnlyList<string> Chunks) : IStep;

internal sealed record Draw(ItemId Item, string Title, string MediaType, IReadOnlyList<string> Chunks) : IStep;

internal sealed record WriteFile(ItemId Item, string Path, string Content) : IStep;

internal sealed record RunCommand(ItemId Item, string Command, string Output, bool AsksPermission) : IStep;

internal sealed record UpdatePlan(IReadOnlyList<PlanStep> Steps) : IStep;

internal sealed record ReportUsage(TokenUsage Tokens, Cost Cost) : IStep;

internal sealed record ReportLimit(UsageLimit Limit) : IStep;

internal sealed record Open(ItemId Item, ItemKind Kind, string Title) : IStep;

internal sealed record Crash(string Reason) : IStep;

internal sealed record Finish : IStep;
