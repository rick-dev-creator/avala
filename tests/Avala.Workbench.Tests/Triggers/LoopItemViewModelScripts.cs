using Avala.Autopilot.Contracts;
using Avala.Testing;
using Avala.Workbench.Triggers;

namespace Avala.Workbench.Tests.Triggers;

public sealed class LoopItemViewModelScripts
{
    private static readonly FakeAutopilot Autopilot = new();

    [Fact]
    public void ARunningLoopSaysHowFarItIsWhatItApprovedWhatWaitsAndItsLatestIterationsFirst() =>
        ViewModelScript.Given(new LoopItemViewModel(Digest(FakeAutopilot.Running), TimeZoneInfo.Utc))
            .Then(item =>
            {
                Assert.Equal(
                    (FakeAutopilot.Running, "/work/ledger-api", "Running · 3 of 20 iterations done", "2 approved on their own · 1 waiting for you · spent 0.42 USD", false),
                    (item.Id, item.Repository, item.Status, item.Summary, item.NeedsAttention));
                Assert.Equal(
                    ["#3 · docs · waiting for review: verification not passed, rule file edited", "#2 · deps · approved on its own", "#1 · greet · approved on its own"],
                    item.Iterations);
            });

    [Fact]
    public void ALoopThatTrippedABreakerSaysWhichAndHowFarAndNeedsAttention() =>
        ViewModelScript.Given(new LoopItemViewModel(Digest(FakeAutopilot.Tripped), TimeZoneInfo.Utc))
            .Then(item => Assert.Equal(("Ended · breaker tripped: the same failure, 2 of 2", true), (item.Status, item.NeedsAttention)));

    [Fact]
    public void ALoopOffersOnlyTheCommandsItsStatusFits()
    {
        var running = Digest(FakeAutopilot.Running);

        Assert.Equal(
            [(true, false, true), (true, false, true), (false, true, true), (false, false, false)],
            new[] { LoopStatus.Running, LoopStatus.Waiting, LoopStatus.Paused, LoopStatus.Ended }
                .Select(status => new LoopItemViewModel(running with { State = running.State with { Status = status } }, TimeZoneInfo.Utc))
                .Select(item => (item.CanPause, item.CanResume, item.CanStop)));
    }

    private static LoopDigest Digest(LoopId loop) => Outcomes.Present(Autopilot.DigestOf(loop));
}
