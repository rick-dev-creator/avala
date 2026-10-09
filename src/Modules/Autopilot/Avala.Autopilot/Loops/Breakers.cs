using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Loops;

internal static class Breakers
{
    public const string IterationUnit = "iterations";

    public const string FailureUnit = "failures";

    extension(IterationOutcome outcome)
    {
        public bool IsFailure => outcome is IterationOutcome.NeedsHelp or IterationOutcome.Failed or IterationOutcome.NotSubmitted;
    }

    extension(LoopRecord loop)
    {
        public Option<BreakerTrip> TrippedBy(IReadOnlyList<Cost> windowSpent, DateTimeOffset now)
        {
            var limits = loop.Request.Limits;
            var trips =
                Count(Breaker.Iterations, IterationUnit, loop.Iterations.Count, limits.Iterations, now)
                    .Concat(Count(Breaker.FailuresInARow, FailureUnit, loop.FailuresInARow, limits.FailuresInARow, now))
                    .Concat(Count(Breaker.SameFailure, loop.LastFailure, loop.SameFailureInARow, limits.SameFailure, now))
                    .Concat(Count(Breaker.NothingChanged, IterationUnit, loop.NothingChangedInARow, limits.NothingChanged, now))
                    .Concat(Spend(Breaker.SpendPerLoop, loop.Spent, limits.SpendPerLoop, now))
                    .Concat(Spend(Breaker.SpendPerWindow, windowSpent, limits.SpendPerWindow, now));

            return trips.FirstOrDefault().ToOption();
        }

        public int FailuresInARow => loop.Iterations.Reverse().TakeWhile(iteration => iteration.Outcome.IsFailure).Count();

        public int SameFailureInARow => loop.Latest.Match(
            last => loop.Iterations.Reverse().TakeWhile(iteration => iteration.Outcome.IsFailure && iteration.Failure == Option<FailureSignature>.Some(last)).Count(),
            () => 0);

        public int NothingChangedInARow => loop.Iterations.Reverse().TakeWhile(iteration => iteration.ChangedNothing).Count();

        private Option<FailureSignature> Latest => loop.Iterations.LastOrDefault().ToOption().Bind(iteration => iteration.Failure);

        private string LastFailure => loop.Latest.Match(failure => $"{failure.Source} {failure.Detail}", () => FailureUnit);
    }

    private static IEnumerable<BreakerTrip> Count(Breaker breaker, string subject, int measured, int cap, DateTimeOffset now) =>
        measured >= cap ? [new BreakerTrip(breaker, subject, measured, cap, now)] : [];

    private static IEnumerable<BreakerTrip> Spend(Breaker breaker, IReadOnlyList<Cost> spent, IReadOnlyList<Cost> caps, DateTimeOffset now) =>
        from cap in caps
        let amount = spent.Where(cost => cost.Currency == cap.Currency).Sum(cost => cost.Amount)
        where amount >= cap.Amount
        select new BreakerTrip(breaker, cap.Currency, amount, cap.Amount, now);
}
