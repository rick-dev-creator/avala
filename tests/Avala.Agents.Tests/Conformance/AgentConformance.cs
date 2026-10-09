using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Turns;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Agents.Tests.Conformance;

internal static class AgentConformance
{
    private static readonly SessionOptions Unrestricted = new(".", PermissionMode.AllowAll);

    public static HarnessTool CanvasTool { get; } = new(
        "canvas",
        "Draw a canvas the user sees beside the conversation.",
        """{ "type": "object", "properties": { "title": { "type": "string" }, "mediaType": { "type": "string" }, "content": { "type": "string" } } }""",
        ToolSurface.Canvas);

    public static HarnessTool ExecutedTool { get; } = new(
        "propose_follow_up",
        "Propose a task to do after this one.",
        """{ "type": "object", "properties": { "instruction": { "type": "string" } }, "required": ["instruction"] }""",
        ToolSurface.Executed);

    public static ToolResult KitResult(ItemId item) => new(item, "Answered by the conformance kit.");

    public static Task<IReadOnlyList<string>> CheckTurnAsync(IAgentProvider provider, CancellationToken deadline) =>
        CheckTurnAsync(provider, Unrestricted, new UserTurn("conformance"), deadline);

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

    public static async Task<IReadOnlyList<string>> CheckHarnessToolAsync(
        IAgentProvider provider,
        SessionOptions options,
        UserTurn instruction,
        CancellationToken deadline)
    {
        if (!provider.Capabilities.AcceptsTools)
        {
            return await CheckTurnAsync(provider, options with { Tools = [] }, instruction, deadline);
        }

        var replies = Allowing with
        {
            Tool = async (session, called, token) =>
                [
                    .. (await session.ReturnAsync(KitResult(new ItemId($"not-{called.Item.Value}")), token)).Match<IReadOnlyList<string>>(
                        _ => ["a result for a call that is not pending was accepted"],
                        _ => []),
                    .. await ReturnAsync(session, called, token),
                ],
            AfterTurn = (_, events, _) => Task.FromResult<IReadOnlyList<string>>(events.OfType<ToolCalled>().LastOrDefault() is { } last
                ? events.OfType<ToolReturned>().Any(returned => returned.Result == KitResult(last.Item))
                    ? []
                    : [$"the result of the call {last.Item.Value} was not reported"]
                : [$"no call of the tool {ExecutedTool.Name} was made"]),
        };

        return (await RunAsync(provider, options with { Tools = [ExecutedTool] }, instruction, replies, deadline)).Violations;
    }

    public static async Task<IReadOnlyList<string>> CheckParallelToolCallsAsync(
        IAgentProvider provider,
        SessionOptions options,
        HarnessTool tool,
        UserTurn instruction,
        CancellationToken deadline)
    {
        if (!provider.Capabilities.AcceptsTools)
        {
            return await CheckTurnAsync(provider, options with { Tools = [] }, instruction, deadline);
        }

        var held = new List<ToolCalled>();
        var replies = Allowing with
        {
            Tool = async (session, called, token) =>
            {
                held.Add(called);

                if (held.Count < 2)
                {
                    return [];
                }

                var violations = new List<string>();

                foreach (var withheld in Enumerable.Reverse(held))
                {
                    violations.AddRange(await ReturnAsync(session, withheld, token));
                }

                held.Clear();

                return violations;
            },
            AfterTurn = (_, events, _) => Task.FromResult<IReadOnlyList<string>>(
                events.OfType<ToolCalled>().Count() < 2
                    ? [$"fewer than two calls of the tool {tool.Name} were pending at once"]
                    : [
                        .. events.OfType<ToolCalled>()
                            .Where(called => !events.OfType<ToolReturned>().Any(returned => returned.Result == KitResult(called.Item)))
                            .Select(called => $"the result of the call {called.Item.Value} was not reported"),
                    ]),
        };

        return (await RunAsync(provider, options with { Tools = [tool] }, instruction, replies, deadline)).Violations;
    }

    public static async Task<IReadOnlyList<string>> CheckFormsAsync(
        IAgentProvider provider,
        SessionOptions options,
        UserTurn instruction,
        CancellationToken deadline)
    {
        if (!provider.Capabilities.AsksQuestions)
        {
            return await CheckTurnAsync(provider, options, instruction, deadline);
        }

        var replies = Allowing with
        {
            Form = async (session, requested, token) =>
                [
                    .. await RefusedAsync(session, Fill(new ItemId($"not-{requested.Item.Value}"), requested.Form), "a form that is not open", token),
                    .. await FillAsync(session, requested, token),
                ],
            AfterTurn = async (session, events, token) => events.OfType<FormRequested>().LastOrDefault() is { } last
                ? await RefusedAsync(session, Fill(last.Item, last.Form), "a form that was already answered", token)
                : ["no form was asked although the provider declares AsksQuestions"],
        };

        return (await RunAsync(provider, options, instruction, replies, deadline)).Violations;
    }

    public static async Task<IReadOnlyList<string>> CheckDenialAsync(
        IAgentProvider provider,
        SessionOptions options,
        UserTurn instruction,
        CancellationToken deadline)
    {
        var replies = Allowing with
        {
            Permission = requested => new PermissionDecision(requested.Item, PermissionAnswer.Deny) { Message = "Denied by the conformance kit." },
        };
        var run = await RunAsync(provider, options, instruction, replies, deadline);
        var denied = run.Events.OfType<PermissionRequested>().Select(requested => requested.Item).ToHashSet();
        var afterDenial = run.Events.SkipWhile(agentEvent => agentEvent is not PermissionResolved).ToList();

        return
        [
            .. run.Violations,
            .. denied.Count == 0 ? ["no permission was requested to deny"] : Array.Empty<string>(),
            .. afterDenial
                .Where(agentEvent => agentEvent is ItemProgressed progressed && denied.Contains(progressed.Item)
                    || agentEvent is ItemCompleted { Outcome: ItemOutcome.Succeeded } completed && denied.Contains(completed.Item))
                .Select(agentEvent => $"the denied item went ahead: {Name(agentEvent)}"),
        ];
    }

    public static async Task<IReadOnlyList<string>> CheckProcessesAsync(
        IAgentProvider provider,
        SessionOptions options,
        UserTurn instruction,
        CancellationToken deadline)
    {
        await using var trees = new RecordingProcessTrees();
        var tree = Assert.IsType<RecordingTree>(await trees.OpenAsync(options.WorkingDirectory, deadline));
        var run = await RunAsync(provider, options with { Processes = tree }, instruction, deadline);

        return tree.Started.Count == 0 ? [.. run.Violations, "no process was started through the session's launcher"] : run.Violations;
    }

    public static async Task<IReadOnlyList<string>> CheckConnectionsAsync(
        IAgentProvider provider,
        SessionOptions first,
        SessionOptions second,
        UserTurn instruction,
        CancellationToken deadline)
    {
        var one = await RunAsync(provider, first, instruction, deadline);
        var other = await RunAsync(provider, second, instruction, deadline);

        return
        [
            .. one.Violations,
            .. other.Violations,
            .. Shared(one.Session, other.Session, session => $"the two connections share the session {session.Value}"),
            .. Shared(one.Account, other.Account, account => $"the two connections share the account {account.Id}"),
            .. Shared(one.Token, other.Token, token => $"the two connections share the resume token {token.Value}"),
            .. await one.Token.Match(
                token => ForeignResumeAsync(provider, second with { Resume = token }, deadline),
                () => Task.FromResult<IReadOnlyList<string>>([])),
        ];
    }

    private static IEnumerable<string> Shared<T>(Option<T> one, Option<T> other, Func<T, string> violation)
        where T : notnull =>
        one.IsSome && one == other ? [one.Match(violation, () => string.Empty)] : [];

    private static async Task<IReadOnlyList<string>> ForeignResumeAsync(IAgentProvider provider, SessionOptions options, CancellationToken deadline)
    {
        if (!provider.Capabilities.CanResume || !(await provider.StartAsync(options, deadline)).TryGetValue(out var session, out _))
        {
            return [];
        }

        await session.DisposeAsync();

        return ["a resume token of one connection was accepted on another"];
    }

    private static Task<Run> RunAsync(IAgentProvider provider, SessionOptions options, UserTurn instruction, CancellationToken deadline) =>
        RunAsync(provider, options, instruction, Allowing, deadline);

    private static async Task<Run> RunAsync(IAgentProvider provider, SessionOptions options, UserTurn instruction, Replies replies, CancellationToken deadline)
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

                var audit = await AuditAsync(session, turn, new Rules(options, provider.Capabilities), replies, deadline);
                audit = audit with
                {
                    Violations = [.. audit.Violations, .. await replies.AfterTurn(session, audit.Events, deadline)],
                    Session = session.Id,
                    Account = account,
                };

                return session.Account == account ? audit : audit with { Violations = [.. audit.Violations, "the account changed during the session"] };
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return new Run(["the turn did not complete before the deadline"], []);
        }
    }

    private static async Task<Run> AuditAsync(IAgentSession session, TurnId expected, Rules rules, Replies replies, CancellationToken deadline)
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

            violations.AddRange(await ReplyAsync(session, agentEvent, replies, kinds, asked, deadline));

            if (agentEvent is TurnCompleted completed)
            {
                return new Run(completed.Outcome == TurnOutcome.Finished ? violations : [.. violations, $"the turn ended {completed.Outcome}"], events);
            }
        }

        return new Run([.. violations, "the event stream ended before TurnCompleted"], events);
    }

    private static async Task<IEnumerable<string>> ReplyAsync(
        IAgentSession session,
        IAgentEvent agentEvent,
        Replies replies,
        Dictionary<ItemId, ItemKind> kinds,
        HashSet<ItemId> asked,
        CancellationToken deadline)
    {
        switch (agentEvent)
        {
            case PermissionRequested requested:
                asked.Add(requested.Item);

                return [.. Describes(requested, kinds), .. await RespondAsync(session, replies.Permission(requested), deadline)];
            case FormRequested form:
                return await replies.Form(session, form, deadline);
            case ToolCalled called:
                return await replies.Tool(session, called, deadline);
            default:
                return [];
        }
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

    private static async Task<IReadOnlyList<string>> RespondAsync(IAgentSession session, PermissionDecision decision, CancellationToken deadline) =>
        (await session.RespondAsync(decision, deadline)).Match<IReadOnlyList<string>>(
            _ => [],
            error => [$"the permission for {decision.Item.Value} could not be answered: {error}"]);

    private static async Task<IReadOnlyList<string>> FillAsync(IAgentSession session, FormRequested requested, CancellationToken deadline) =>
        (await session.AnswerAsync(Fill(requested.Item, requested.Form), deadline)).Match<IReadOnlyList<string>>(
            _ => [],
            error => [$"the form {requested.Item.Value} could not be answered: {error}"]);

    private static async Task<IReadOnlyList<string>> ReturnAsync(IAgentSession session, ToolCalled called, CancellationToken deadline) =>
        (await session.ReturnAsync(KitResult(called.Item), deadline)).Match<IReadOnlyList<string>>(
            _ => [],
            error => [$"the result of the call {called.Item.Value} could not be returned: {error}"]);

    private static async Task<IReadOnlyList<string>> RefusedAsync(IAgentSession session, FormAnswer answer, string form, CancellationToken deadline) =>
        (await session.AnswerAsync(answer, deadline)).Match<IReadOnlyList<string>>(
            _ => [$"an answer to {form} was accepted"],
            _ => []);

    private static FormAnswer Fill(ItemId item, AgentForm form) =>
        new(item, [.. form.Fields.Select(field => field.Kind switch
        {
            FieldKind.Confirmation => new FieldAnswer(field.Id) { Confirmed = true },
            FieldKind.FreeText => new FieldAnswer(field.Id) { Text = "conformance" },
            _ => new FieldAnswer(field.Id) { Chosen = [(field.Options.FirstOrDefault(option => option.Recommended) ?? field.Options[0]).Label] },
        })]);

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

    private sealed record Run(IReadOnlyList<string> Violations, IReadOnlyList<IAgentEvent> Events)
    {
        public Option<SessionId> Session { get; init; }

        public Option<AgentAccount> Account { get; init; }

        public Option<ResumeToken> Token => Events.OfType<ResumeTokenIssued>().LastOrDefault() is { } issued ? issued.Token : Option<ResumeToken>.None;
    }

    private sealed record Rules(SessionOptions Options, AgentCapabilities Capabilities)
    {
        public bool AsksEveryTime => Options.Permissions == PermissionMode.AskEveryTime;

        public IEnumerable<string> Breaches(IAgentEvent agentEvent) => agentEvent switch
        {
            ResumeTokenIssued when !Capabilities.CanResume => ["a resume token was issued although the provider does not declare CanResume"],
            CanvasStarted started when !Options.Tools.Any(tool => tool.Surface == ToolSurface.Canvas) =>
                [$"the canvas {started.Item.Value} was drawn without the canvas tool"],
            FormRequested asked when !Capabilities.AsksQuestions => [$"the form {asked.Item.Value} was asked although the provider does not declare AsksQuestions"],
            ToolCalled called when !Options.Tools.Any(tool => tool.Name == called.Tool && tool.Surface == ToolSurface.Executed) =>
                [$"the tool {called.Tool} was called although the session was not given it"],
            _ => [],
        };
    }

    private sealed record Replies(
        Func<PermissionRequested, PermissionDecision> Permission,
        Func<IAgentSession, FormRequested, CancellationToken, Task<IReadOnlyList<string>>> Form,
        Func<IAgentSession, ToolCalled, CancellationToken, Task<IReadOnlyList<string>>> Tool,
        Func<IAgentSession, IReadOnlyList<IAgentEvent>, CancellationToken, Task<IReadOnlyList<string>>> AfterTurn);

    private static Replies Allowing { get; } = new(
        requested => new PermissionDecision(requested.Item, PermissionAnswer.Allow),
        FillAsync,
        ReturnAsync,
        (_, _, _) => Task.FromResult<IReadOnlyList<string>>([]));
}
