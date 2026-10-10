using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Tests.Conformance;

internal static class CapabilityConformance
{
    public static async Task<IReadOnlyList<string>> CheckReportsAsync(
        IAgentProvider provider,
        SessionOptions options,
        UserTurn instruction,
        CancellationToken deadline)
    {
        var declared = provider.CapabilitiesOn(options.Connection);
        var run = await AgentConformance.RunAsync(provider, options, instruction, deadline);

        return
        [
            .. run.Violations,
            .. Missing<ReportsUsage>(declared, run.Events.OfType<UsageReported>().Any(), "no usage was reported although the provider declares ReportsUsage"),
            .. Missing<ReportsCost>(declared, run.Events.OfType<UsageReported>().Any(usage => usage.Cost.IsSome), "no cost was reported although the provider declares ReportsCost"),
            .. Missing<ReportsLimits>(declared, run.Events.OfType<LimitReported>().Any(), "no limit was reported although the provider declares ReportsLimits"),
        ];
    }

    public static async Task<IReadOnlyList<string>> CheckModelChoiceAsync(
        IAgentProvider provider,
        SessionOptions options,
        UserTurn instruction,
        CancellationToken deadline)
    {
        var declared = provider.CapabilitiesOn(options.Connection).Get<OffersModels>();

        if (declared.IsNone)
        {
            return await AgentConformance.CheckTurnAsync(provider, options, instruction, deadline);
        }

        var offered = declared.Match(found => found, () => new OffersModels([], []));
        var choice = new ModelChoice(
            offered.Models.Count > 0 ? offered.Models[^1] : Option<string>.None,
            offered.Efforts.Count > 0 ? offered.Efforts[^1] : Option<string>.None);
        var run = await AgentConformance.RunAsync(provider, options with { Model = choice }, instruction, deadline);
        var reported = run.Events.OfType<ModelReported>().ToList();

        return
        [
            .. run.Violations,
            .. reported.Count == 0 ? ["no model was reported although the provider declares OffersModels"] : Array.Empty<string>(),
            .. reported.SelectMany(report => RanOtherThan(report, offered, choice)),
        ];
    }

    private static IEnumerable<string> RanOtherThan(ModelReported report, OffersModels offered, ModelChoice choice) =>
    [
        .. offered.Models.Contains(report.Model) && choice.Model != Option<string>.Some(report.Model)
            ? [$"the model {report.Model} was reported although {choice.Model.Match(model => model, () => "the default")} was chosen"]
            : Array.Empty<string>(),
        .. choice.Effort.IsSome && report.Effort != choice.Effort
            ? [$"the effort {report.Effort.Match(effort => effort, () => "none")} was reported although {choice.Effort.Match(effort => effort, () => "none")} was chosen"]
            : Array.Empty<string>(),
    ];

    public static async Task<IReadOnlyList<string>> CheckInterruptAsync(
        IAgentProvider provider,
        SessionOptions options,
        UserTurn instruction,
        CancellationToken deadline)
    {
        if (!provider.CapabilitiesOn(options.Connection).Has<Interruptible>())
        {
            return await AgentConformance.CheckTurnAsync(provider, options, instruction, deadline);
        }

        if (!(await provider.StartAsync(options, deadline)).TryGetValue(out var session, out var startError))
        {
            return [$"the session did not start: {startError}"];
        }

        await using (session)
        {
            if (!(await session.SendAsync(instruction, deadline)).TryGetValue(out var turn, out var sendError))
            {
                return [$"the turn was not accepted: {sendError}"];
            }

            var violations = new List<string>();

            await foreach (var agentEvent in session.Events.WithCancellation(deadline))
            {
                switch (agentEvent)
                {
                    case TurnStarted started when started.Turn == turn:
                        violations.AddRange((await session.InterruptAsync(deadline)).Match<IEnumerable<string>>(
                            interrupted => interrupted == turn ? [] : [$"the interruption ended the turn {interrupted.Value} instead of {turn.Value}"],
                            error => [$"the interruption was refused: {error}"]));
                        break;
                    case TurnCompleted completed when completed.Turn == turn:
                        return completed.Outcome == TurnOutcome.Interrupted ? violations : [.. violations, $"the interrupted turn ended {completed.Outcome}"];
                }
            }

            return [.. violations, "the event stream ended before TurnCompleted"];
        }
    }

    public const string Steering = "Keep the old namespace as an alias.";

    public static async Task<IReadOnlyList<string>> CheckMidTurnAsync(
        IAgentProvider provider,
        SessionOptions options,
        UserTurn instruction,
        CancellationToken deadline)
    {
        if (!provider.CapabilitiesOn(options.Connection).Has<AcceptsMessagesMidTurn>())
        {
            return await AgentConformance.CheckTurnAsync(provider, options, instruction, deadline);
        }

        if (!(await provider.StartAsync(options, deadline)).TryGetValue(out var session, out var startError))
        {
            return [$"the session did not start: {startError}"];
        }

        await using (session)
        {
            if (!(await session.SendAsync(instruction, deadline)).TryGetValue(out var turn, out var sendError))
            {
                return [$"the turn was not accepted: {sendError}"];
            }

            var violations = new List<string>();
            var queued = false;

            await foreach (var agentEvent in session.Events.WithCancellation(deadline))
            {
                switch (agentEvent)
                {
                    case TurnStarted started when started.Turn == turn:
                        violations.AddRange(Joined(await session.SendAsync(new UserTurn(Steering) { MidTurn = true }, deadline), turn));
                        break;
                    case TurnStarted started:
                        violations.Add($"the message mid-turn started the turn {started.Turn.Value}");
                        break;
                    case MessageQueued message when message.Turn == turn && message.Text == Steering:
                        queued = true;
                        break;
                    case TurnCompleted completed when completed.Turn == turn:
                        return [.. violations, .. Steered(queued, completed.Outcome)];
                }
            }

            return [.. violations, "the event stream ended before TurnCompleted"];
        }
    }

    private static IEnumerable<string> Joined(Avala.Sdk.Result<TurnId, AgentError> sent, TurnId turn) =>
        sent.Match<IEnumerable<string>>(
            joined => joined == turn ? [] : [$"the message mid-turn joined the turn {joined.Value} instead of {turn.Value}"],
            error => [$"the message mid-turn was refused: {error}"]);

    private static IEnumerable<string> Steered(bool queued, TurnOutcome outcome) =>
    [
        .. queued ? [] : new[] { "the message mid-turn was not queued into the running turn" },
        .. outcome == TurnOutcome.Finished ? [] : new[] { $"the steered turn ended {outcome}" },
    ];

    private static IEnumerable<string> Missing<T>(CapabilitySet declared, bool reported, string violation)
        where T : ICapability =>
        declared.Has<T>() && !reported ? [violation] : [];
}
