using System.Globalization;
using System.Text.RegularExpressions;
using Avala.Autopilot.Contracts;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Triggers;

internal static partial class LoopPhrases
{
    public const int IterationsShown = 5;

    public static string Status(LoopDigest digest, TimeZoneInfo zone)
    {
        var state = digest.State;

        return state.Status switch
        {
            LoopStatus.Running => string.Create(CultureInfo.InvariantCulture, $"Running · {state.Iterations} of {state.Limits.Iterations} iterations done"),
            LoopStatus.Waiting => $"Waiting for a recurring task until {Until(state, zone)}",
            LoopStatus.Paused when state.Pause.Match(pause => pause == PauseReason.UsageLimit, () => false) =>
                $"Paused until {Until(state, zone)} · usage window {(digest.Pauses.Count == 0 ? "unknown" : digest.Pauses[^1].Window.Match(window => window, () => "unknown"))}",
            LoopStatus.Paused when state.Pause.Match(pause => pause == PauseReason.DailySpend, () => false) => $"Paused until {Until(state, zone)} · today's spend ceiling",
            LoopStatus.Paused => "Paused by you",
            _ => $"Ended · {Ending(digest)}",
        };
    }

    public static bool NeedsAttention(LoopState state) =>
        state.Ending.Match(ending => ending is LoopEnding.BreakerTripped or LoopEnding.SourceFailed or LoopEnding.Failed, () => false);

    public static string Summary(LoopDigest digest) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{digest.Iterations.Count(iteration => iteration.Outcome == IterationOutcome.ApprovedAutomatically)} approved on their own · {digest.Iterations.Count(Waits)} waiting for you · spent {Amounts.Costs(digest.Spent)}");

    public static IReadOnlyList<string> Iterations(LoopDigest digest) =>
        [.. digest.Iterations.Reverse().Take(IterationsShown).Select(Iteration)];

    public static string Iteration(IterationRecord iteration) =>
        string.Create(CultureInfo.InvariantCulture, $"#{iteration.Number} · {iteration.Task.Key} · {Outcome(iteration.Outcome)}")
        + (iteration.Exceptions.Count == 0 ? string.Empty : $": {string.Join(", ", iteration.Exceptions.Select(exception => Words(exception.ToString())))}")
        + iteration.Hold.Match(hold => $" · held: {Words(hold.ToString())}", () => string.Empty);

    public static string Error(AutopilotError error) =>
        error switch
        {
            AutopilotError.NotRunning => "That loop is already paused.",
            AutopilotError.NotPaused => "That loop is not paused.",
            AutopilotError.LoopEnded => "That loop has ended.",
            AutopilotError.UnknownLoop => "That loop is no longer known.",
            _ => $"Refused: {error}.",
        };

    private static bool Waits(IterationRecord iteration) =>
        iteration.Outcome is IterationOutcome.AwaitingReview or IterationOutcome.AwaitingAnswer or IterationOutcome.NeedsHelp;

    private static string Until(LoopState state, TimeZoneInfo zone) => state.Until.Match(until => Amounts.Time(until, zone), () => "unknown");

    private static string Ending(LoopDigest digest)
    {
        var state = digest.State;

        return state.Ending.Match(ending => ending, () => LoopEnding.Stopped) switch
        {
            LoopEnding.Drained => string.Create(CultureInfo.InvariantCulture, $"drained after {state.Iterations} {(state.Iterations == 1 ? "iteration" : "iterations")}"),
            LoopEnding.BreakerTripped => digest.Breakers.Count == 0 ? "breaker tripped" : Tripped(digest.Breakers[^1]),
            LoopEnding.SourceFailed => $"the task source was refused: {state.Error.Match(error => error.ToString(), () => "unknown")}",
            LoopEnding.Failed => $"failed: {state.Fault.Match(fault => fault, () => "unknown")}",
            _ => "stopped",
        };
    }

    private static string Tripped(BreakerTrip trip) =>
        string.Create(CultureInfo.InvariantCulture, $"breaker tripped: {Measure(trip.Breaker)}, {trip.Measured:0.####} of {trip.Cap:0.####}");

    private static string Measure(Breaker breaker) =>
        breaker switch
        {
            Breaker.Iterations => "iterations",
            Breaker.FailuresInARow => "failures in a row",
            Breaker.SameFailure => "the same failure",
            Breaker.NothingChanged => "nothing changed",
            Breaker.SpendPerLoop => "spending of the loop",
            Breaker.SpendPerWindow => "spending in the window",
            _ => "a usage limit without a reset time",
        };

    private static string Outcome(IterationOutcome outcome) =>
        outcome switch
        {
            IterationOutcome.ApprovedAutomatically => "approved on its own",
            IterationOutcome.AwaitingReview => "waiting for review",
            IterationOutcome.AwaitingAnswer => "waiting for an answer",
            IterationOutcome.NeedsHelp => "needs help",
            IterationOutcome.Failed => "failed",
            IterationOutcome.NotSubmitted => "not submitted",
            IterationOutcome.ApprovedByPerson => "approved by you",
            IterationOutcome.CarryingOn => "carrying on",
            _ => "discarded",
        };

    private static string Words(string name) => WordStart().Replace(name, " $1").Trim().ToLowerInvariant();

    [GeneratedRegex("([A-Z])", RegexOptions.None, 1000)]
    private static partial Regex WordStart();
}
