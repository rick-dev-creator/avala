using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Triggers;

namespace Avala.Workbench.Tests.Triggers;

public sealed class LoopPhrasesTests
{
    private static readonly DateTimeOffset Now = FakeTriggers.Now;

    [Fact]
    public void EachStatusOfALoopReadsAsOneLineInTheComputersTimeZone()
    {
        var waiting = State(LoopStatus.Waiting) with { Until = Now.AddHours(1) };
        var limited = State(LoopStatus.Paused) with { Pause = PauseReason.UsageLimit, Until = Now.AddHours(2) };
        var pause = new LoopPause(PauseReason.UsageLimit, Now, Now.AddHours(2)) { Window = "5h" };

        Assert.Equal(
            [
                "Waiting for a recurring task until Fri 9 Oct 16:00",
                "Paused until Fri 9 Oct 17:00 · usage window 5h",
                "Paused by you",
                "Ended · drained after 1 iteration",
                "Ended · stopped",
                "Ended · the task source was refused: InvalidKey",
                "Ended · failed: IOException: The tracker went away",
            ],
            [
                LoopPhrases.Status(Digest(waiting), TriggerZones.Madrid),
                LoopPhrases.Status(Digest(limited) with { Pauses = [pause] }, TriggerZones.Madrid),
                LoopPhrases.Status(Digest(State(LoopStatus.Paused) with { Pause = PauseReason.Command }), TriggerZones.Madrid),
                LoopPhrases.Status(Digest(Ended(LoopEnding.Drained) with { Iterations = 1 }), TriggerZones.Madrid),
                LoopPhrases.Status(Digest(Ended(LoopEnding.Stopped)), TriggerZones.Madrid),
                LoopPhrases.Status(Digest(Ended(LoopEnding.SourceFailed) with { Error = AutopilotError.InvalidKey }), TriggerZones.Madrid),
                LoopPhrases.Status(Digest(Ended(LoopEnding.Failed) with { Fault = "IOException: The tracker went away" }), TriggerZones.Madrid),
            ]);
    }

    [Fact]
    public void ALoopThatTrippedABreakerOrFailedNeedsAttentionAndOneDrainedOrStoppedDoesNot() =>
        Assert.Equal(
            [true, true, true, false, false, false],
            [.. new[] { LoopEnding.BreakerTripped, LoopEnding.SourceFailed, LoopEnding.Failed, LoopEnding.Drained, LoopEnding.Stopped }.Select(ending => LoopPhrases.NeedsAttention(Ended(ending))), LoopPhrases.NeedsAttention(State(LoopStatus.Running))]);

    [Fact]
    public void AnIterationHeldForAPersonSaysWhy() =>
        Assert.Equal(
            "#4 · deps · needs help · held: limit nearly reached",
            LoopPhrases.Iteration(new IterationRecord(4, new SourcedTask("backlog", "deps", "/work/ledger-api", "Update"), JobId.New(), IterationOutcome.NeedsHelp, Now) { Hold = HoldReason.LimitNearlyReached }));

    private static LoopState State(LoopStatus status) => new(LoopId.New(), "/work/ledger-api", status, Now, 0, Option<JobId>.None);

    private static LoopState Ended(LoopEnding ending) => State(LoopStatus.Ended) with { Ending = ending };

    private static LoopDigest Digest(LoopState state) => new(state, [], [], [], [], []);
}
