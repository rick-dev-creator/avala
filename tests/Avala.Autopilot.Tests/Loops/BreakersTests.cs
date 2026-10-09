using Avala.Autopilot.Contracts;
using Avala.Autopilot.Loops;
using Avala.Autopilot.Tests.Looping;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;

namespace Avala.Autopilot.Tests.Loops;

public sealed class BreakersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 23, 0, 0, TimeSpan.Zero);

    private static readonly FailureSignature Tests = new(FailureSource.Check, "tests Failed exit 1");

    [Fact]
    public void FailuresInARowCountOnlyTheFailuresSinceTheLastIterationThatDidNotFail()
    {
        var loop = Loop(new LoopLimits { FailuresInARow = 2, SameFailure = 9 }, Failed(Tests), Approved(), Failed(Tests), Failed(new FailureSignature(FailureSource.Hold, "Stalled")));

        Assert.Equal((2, 1), (loop.FailuresInARow, loop.SameFailureInARow));
        Assert.Equal(Breaker.FailuresInARow, Outcomes.Present(loop.TrippedBy([], Now)).Breaker);
        Assert.True((loop with { Iterations = loop.Iterations.RemoveAt(3) }).TrippedBy([], Now).IsNone);
    }

    [Fact]
    public void IterationsThatChangeNothingInARowTripTheirBreaker()
    {
        var loop = Loop(new LoopLimits { NothingChanged = 2 }, Approved() with { ChangedNothing = true }, Approved() with { ChangedNothing = true });

        Assert.Equal(new BreakerTrip(Breaker.NothingChanged, "iterations", 2, 2, Now), Outcomes.Present(loop.TrippedBy([], Now)));
        Assert.True((loop with { Iterations = [loop.Iterations[0], Approved(), loop.Iterations[1]] }).TrippedBy([], Now).IsNone);
    }

    [Fact]
    public void TheMaximumNumberOfIterationsTripsItsBreaker() =>
        Assert.Equal(
            new BreakerTrip(Breaker.Iterations, "iterations", 3, 3, Now),
            Outcomes.Present(Loop(new LoopLimits { Iterations = 3 }, Approved(), Approved(), Approved()).TrippedBy([], Now)));

    [Fact]
    public void ACapOnSpendingCountsOnlyItsCurrency() =>
        Assert.True(Loop(new LoopLimits { SpendPerLoop = [Pilot.Usd(1m)] }, Approved() with { Cost = [new Agents.Contracts.Events.Cost(5m, "EUR"), Pilot.Usd(0.99m)] })
            .TrippedBy([new Agents.Contracts.Events.Cost(9m, "EUR")], Now)
            .IsNone);

    public static TheoryData<string, string, string> Signatures => new()
    {
        { "hold", "Hold", "Stalled" },
        { "failed check", "Check", "tests TimedOut" },
        { "invalid declaration", "Declaration", "InvalidDeclaration" },
        { "no evidence", "Job", "Failed" },
    };

    [Theory]
    [MemberData(nameof(Signatures))]
    public void AFailureIsSignedByItsHoldItsFirstFailingCheckItsDeclarationOrItsOutcome(string evidence, string source, string detail)
    {
        var job = JobId.New();
        var timedOut = new CheckEvidence("tests", "dotnet test", CheckStatus.TimedOut, Option<int>.None, TimeSpan.FromMinutes(10), string.Empty, string.Empty);
        var built = new CheckEvidence("build", "dotnet build", CheckStatus.Passed, 0, TimeSpan.FromSeconds(3), string.Empty, string.Empty);
        var signature = evidence switch
        {
            "hold" => FailureSignatures.Of(IterationOutcome.NeedsHelp, HoldReason.Stalled, Pilot.Report(job, VerificationOutcome.Failed, timedOut)),
            "failed check" => FailureSignatures.Of(IterationOutcome.NeedsHelp, Option<HoldReason>.None, Pilot.Report(job, VerificationOutcome.Failed, built, timedOut)),
            "invalid declaration" => FailureSignatures.Of(IterationOutcome.NeedsHelp, Option<HoldReason>.None, Pilot.Report(job, VerificationOutcome.InvalidDeclaration)),
            _ => FailureSignatures.Of(IterationOutcome.Failed, Option<HoldReason>.None, Option<VerificationReport>.None),
        };

        Assert.Equal(new FailureSignature(Enum.Parse<FailureSource>(source), detail), signature);
    }

    private static LoopRecord Loop(LoopLimits limits, params IterationRecord[] iterations) =>
        LoopRecord.Begin(LoopId.New(), new LoopRequest(Pilot.Repository) { Limits = limits }, Now) with { Iterations = [.. iterations] };

    private static IterationRecord Approved() => Iteration(IterationOutcome.ApprovedAutomatically);

    private static IterationRecord Failed(FailureSignature failure) => Iteration(IterationOutcome.NeedsHelp) with { Failure = failure };

    private static IterationRecord Iteration(IterationOutcome outcome) =>
        new(1, new SourcedTask("backlog", "task", Pilot.Repository, "Do it"), JobId.New(), outcome, Now);
}
