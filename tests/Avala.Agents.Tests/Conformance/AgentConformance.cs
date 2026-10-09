using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Turns;
using Avala.Sdk;

namespace Avala.Agents.Tests.Conformance;

internal static class AgentConformance
{
    private static readonly SessionOptions Options = new(".", PermissionMode.AllowAll);

    public static HarnessTool CanvasTool { get; } = new(
        "canvas",
        "Draw a canvas the user sees beside the conversation.",
        """{ "type": "object", "properties": { "title": { "type": "string" }, "mediaType": { "type": "string" }, "content": { "type": "string" } } }""",
        ToolSurface.Canvas);

    public static Task<IReadOnlyList<string>> CheckTurnAsync(IAgentProvider provider, CancellationToken deadline) =>
        CheckTurnAsync(provider, Options, new UserTurn("conformance"), deadline);

    public static async Task<IReadOnlyList<string>> CheckTurnAsync(
        IAgentProvider provider,
        SessionOptions options,
        UserTurn instruction,
        CancellationToken deadline) =>
        (await RunAsync(provider, options, instruction, deadline)).Violations;

    public static async Task<IReadOnlyList<string>> CheckResumeAsync(
        IAgentProvider provider,
        SessionOptions options,
        UserTurn instruction,
        CancellationToken deadline)
    {
        var first = await RunAsync(provider, options, instruction, deadline);

        if (!provider.Capabilities.CanResume)
        {
            return first.Violations;
        }

        if (first.Events.OfType<ResumeTokenIssued>().LastOrDefault() is not { } issued)
        {
            return [.. first.Violations, "no resume token was issued although the provider declares CanResume"];
        }

        var resumed = await RunAsync(provider, options with { Resume = issued.Token }, new UserTurn("continue"), deadline);

        return [.. first.Violations, .. resumed.Violations];
    }

    public static async Task<IReadOnlyList<string>> CheckCanvasToolAsync(
        IAgentProvider provider,
        SessionOptions options,
        UserTurn instruction,
        CancellationToken deadline)
    {
        if (!provider.Capabilities.AcceptsTools)
        {
            return await CheckTurnAsync(provider, options with { Tools = [] }, instruction, deadline);
        }

        var run = await RunAsync(provider, options with { Tools = [CanvasTool] }, instruction, deadline);
        var drawn = run.Events.OfType<CanvasStarted>().Select(started => started.Item).ToHashSet();

        return run.Events.OfType<ItemCompleted>().Any(completed => completed.Outcome == ItemOutcome.Succeeded && drawn.Contains(completed.Item))
            ? run.Violations
            : [.. run.Violations, "no canvas was drawn through the canvas tool"];
    }

    private static async Task<Run> RunAsync(IAgentProvider provider, SessionOptions options, UserTurn instruction, CancellationToken deadline)
    {
        try
        {
            if (!(await provider.StartAsync(options, deadline)).TryGetValue(out var session, out var startError))
            {
                return new Run(
                    [options.Resume.IsSome ? $"the resume token was not accepted: {startError}" : $"the session did not start: {startError}"],
                    []);
            }

            await using (session)
            {
                var account = session.Account;

                if (!(await session.SendAsync(instruction, deadline)).TryGetValue(out var turn, out var sendError))
                {
                    return new Run([$"the turn was not accepted: {sendError}"], []);
                }

                var audit = await AuditAsync(session, turn, new Rules(options, provider.Capabilities), deadline);

                return session.Account == account ? audit : audit with { Violations = [.. audit.Violations, "the account changed during the session"] };
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return new Run(["the turn did not complete before the deadline"], []);
        }
    }

    private static async Task<Run> AuditAsync(IAgentSession session, TurnId expected, Rules rules, CancellationToken deadline)
    {
        var violations = new List<string>();
        var events = new List<IAgentEvent>();
        var kinds = new Dictionary<ItemId, ItemKind>();
        var asked = new HashSet<ItemId>();
        Turn? turn = null;

        await foreach (var agentEvent in session.Events.WithCancellation(deadline))
        {
            events.Add(agentEvent);
            violations.AddRange(agentEvent.Turn != expected
                ? [$"{Name(agentEvent)} belongs to another turn"]
                : Audit(ref turn, agentEvent));
            violations.AddRange(rules.Breaches(agentEvent));

            if (agentEvent is ItemStarted started)
            {
                kinds[started.Item] = started.Kind;
            }

            if (rules.AsksEveryTime)
            {
                violations.AddRange(Unasked(agentEvent, kinds, asked));
            }

            if (agentEvent is PermissionRequested requested)
            {
                asked.Add(requested.Item);
                violations.AddRange(Describes(requested, kinds));
                violations.AddRange(await AllowAsync(session, requested, deadline));
            }

            if (agentEvent is TurnCompleted)
            {
                return new Run(violations, events);
            }
        }

        return new Run([.. violations, "the event stream ended before TurnCompleted"], events);
    }

    private static IEnumerable<string> Unasked(IAgentEvent agentEvent, Dictionary<ItemId, ItemKind> kinds, HashSet<ItemId> asked)
    {
        var acted = agentEvent switch
        {
            ItemProgressed progressed => progressed.Item,
            ItemCompleted { Outcome: ItemOutcome.Succeeded } completed => completed.Item,
            _ => Option<ItemId>.None,
        };

        return acted.Match<IEnumerable<string>>(
            item => kinds.TryGetValue(item, out var kind) && kind is ItemKind.FileEdit or ItemKind.Command && asked.Add(item)
                ? [$"the {kind} {item.Value} went ahead without asking permission"]
                : [],
            () => []);
    }

    private static IEnumerable<string> Describes(PermissionRequested requested, Dictionary<ItemId, ItemKind> kinds)
    {
        if (string.IsNullOrWhiteSpace(requested.Target))
        {
            yield return $"the permission for {requested.Item.Value} names no target";
        }

        if (kinds.TryGetValue(requested.Item, out var kind) && kind != requested.Kind)
        {
            yield return $"the permission for {requested.Item.Value} is for a {requested.Kind} but its item is a {kind}";
        }
    }

    private static async Task<IReadOnlyList<string>> AllowAsync(IAgentSession session, PermissionRequested requested, CancellationToken deadline) =>
        (await session.RespondAsync(new PermissionDecision(requested.Item, PermissionAnswer.Allow), deadline)).Match<IReadOnlyList<string>>(
            _ => [],
            error => [$"the permission for {requested.Item.Value} could not be granted: {error}"]);

    private static IEnumerable<string> Audit(ref Turn? turn, IAgentEvent agentEvent)
    {
        if (turn is null)
        {
            return agentEvent is TurnStarted started && Turn.Begin(started).TryGetValue(out turn, out _)
                ? []
                : [$"{Name(agentEvent)} arrived before TurnStarted"];
        }

        return turn.Apply(agentEvent, TimeProvider.System.GetUtcNow()).Match(
            progress => progress.Events
                .OfType<ItemCompleted>()
                .Where(completed => completed.Outcome == ItemOutcome.Abandoned)
                .Select(completed => $"item {completed.Item.Value} was left open"),
            error => [$"{Name(agentEvent)} was rejected: {error}"]);
    }

    private static string Name(IAgentEvent agentEvent) => agentEvent.GetType().Name;

    private sealed record Run(IReadOnlyList<string> Violations, IReadOnlyList<IAgentEvent> Events);

    private sealed record Rules(SessionOptions Options, AgentCapabilities Capabilities)
    {
        public bool AsksEveryTime => Options.Permissions == PermissionMode.AskEveryTime;

        public IEnumerable<string> Breaches(IAgentEvent agentEvent) => agentEvent switch
        {
            ResumeTokenIssued when !Capabilities.CanResume => ["a resume token was issued although the provider does not declare CanResume"],
            CanvasStarted started when !Options.Tools.Any(tool => tool.Surface == ToolSurface.Canvas) =>
                [$"the canvas {started.Item.Value} was drawn without the canvas tool"],
            _ => [],
        };
    }
}
