using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Turns;

namespace Avala.Agents.Tests.Conformance;

internal static class AgentConformance
{
    private static readonly SessionOptions Options = new(".", PermissionMode.AllowAll);

    public static Task<IReadOnlyList<string>> CheckTurnAsync(IAgentProvider provider, CancellationToken deadline) =>
        CheckTurnAsync(provider, Options, new UserTurn("conformance"), deadline);

    public static async Task<IReadOnlyList<string>> CheckTurnAsync(
        IAgentProvider provider,
        SessionOptions options,
        UserTurn instruction,
        CancellationToken deadline)
    {
        try
        {
            if (!(await provider.StartAsync(options, deadline)).TryGetValue(out var session, out var startError))
            {
                return [$"the session did not start: {startError}"];
            }

            await using (session)
            {
                return (await session.SendAsync(instruction, deadline)).TryGetValue(out var turn, out var sendError)
                    ? await AuditAsync(session, turn, deadline)
                    : [$"the turn was not accepted: {sendError}"];
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return ["the turn did not complete before the deadline"];
        }
    }

    private static async Task<IReadOnlyList<string>> AuditAsync(IAgentSession session, TurnId expected, CancellationToken deadline)
    {
        var violations = new List<string>();
        var kinds = new Dictionary<ItemId, ItemKind>();
        Turn? turn = null;

        await foreach (var agentEvent in session.Events.WithCancellation(deadline))
        {
            violations.AddRange(agentEvent.Turn != expected
                ? [$"{Name(agentEvent)} belongs to another turn"]
                : Audit(ref turn, agentEvent));

            if (agentEvent is ItemStarted started)
            {
                kinds[started.Item] = started.Kind;
            }

            if (agentEvent is PermissionRequested requested)
            {
                violations.AddRange(Describes(requested, kinds));
                violations.AddRange(await AllowAsync(session, requested, deadline));
            }

            if (agentEvent is TurnCompleted)
            {
                return violations;
            }
        }

        return [.. violations, "the event stream ended before TurnCompleted"];
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
}
