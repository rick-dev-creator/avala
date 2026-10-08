using Avala.Agents.Contracts.Events;
using Avala.Observability.Contracts;

namespace Avala.Observability.Usage;

internal static class UsageArithmetic
{
    extension(TokenUsage)
    {
        public static TokenUsage operator +(TokenUsage left, TokenUsage right) => new(
            left.Input + right.Input,
            left.Output + right.Output,
            left.CacheRead + right.CacheRead,
            left.CacheWrite + right.CacheWrite,
            left.Reasoning + right.Reasoning);
    }

    extension(TurnTally)
    {
        public static TurnTally operator +(TurnTally left, TurnTally right) => new(
            left.Finished + right.Finished,
            left.Interrupted + right.Interrupted,
            left.Failed + right.Failed,
            left.Duration + right.Duration);
    }

    extension(TurnTally tally)
    {
        public TurnTally Counting(TurnOutcome outcome, TimeSpan duration) => tally + outcome switch
        {
            TurnOutcome.Interrupted => new TurnTally(0, 1, 0, duration),
            TurnOutcome.Failed => new TurnTally(0, 0, 1, duration),
            _ => new TurnTally(1, 0, 0, duration),
        };
    }
}
