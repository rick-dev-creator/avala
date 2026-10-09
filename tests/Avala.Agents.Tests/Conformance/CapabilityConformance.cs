using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

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

    private static IEnumerable<string> Missing<T>(CapabilitySet declared, bool reported, string violation)
        where T : ICapability =>
        declared.Has<T>() && !reported ? [violation] : [];
}
