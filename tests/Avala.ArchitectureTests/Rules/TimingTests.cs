using Avala.ArchitectureTests.Scopes;
using Avala.ArchitectureTests.Timing;

namespace Avala.ArchitectureTests.Rules;

public sealed class TimingTests
{
    private const string Fixture = "tests/Avala.ArchitectureTests.Fixtures/Violating/Application/ImpatientLedger.cs";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProductionAndTestsWaitOnEventsNotOnTimeAsync()
    {
        var waits = await TimedWaitRules.WaitOnTimeAsync(TimedWaitRules.ProductionFiles, Cancellation);
        var exceptions = await TimedWaitRules.DocumentedExceptionsAsync(Cancellation);

        var undocumented = TimedWaitRules.Undocumented(waits, exceptions);

        Assert.True(undocumented.Count == 0, string.Join(Environment.NewLine, undocumented));
    }

    [Fact]
    public async Task EveryDocumentedExceptionStillWaitsOnTimeAsync()
    {
        var waits = await TimedWaitRules.WaitOnTimeAsync(TimedWaitRules.ProductionFiles, Cancellation);
        var exceptions = await TimedWaitRules.DocumentedExceptionsAsync(Cancellation);

        var unused = TimedWaitRules.Unused(waits, exceptions);

        Assert.True(unused.Count == 0, string.Join(Environment.NewLine, unused));
    }

    [Fact]
    public async Task AcceptsAwaitedSignalsAndTimeProviderTimersAsync() =>
        Assert.Empty(await TimedWaitRules.WaitOnTimeAsync(TimedWaitRules.FilesIn(CodeScopes.Of(Scope.Compliant).SourceDirectory), Cancellation));

    [Fact]
    public async Task DetectsSleepsDelaysSpinsAndTimersWithTheirLineAsync() =>
        Assert.Equal(
            [
                new TimedWait(Fixture, 5, "Thread.Sleep"),
                new TimedWait(Fixture, 7, "Task.Delay"),
                new TimedWait(Fixture, 9, "Task.Delay"),
                new TimedWait(Fixture, 11, "Thread.SpinWait"),
                new TimedWait(Fixture, 13, "SpinWait"),
                new TimedWait(Fixture, 15, "Timer"),
                new TimedWait(Fixture, 17, "Timer"),
                new TimedWait(Fixture, 19, "PeriodicTimer"),
            ],
            await TimedWaitRules.WaitOnTimeAsync(TimedWaitRules.FilesIn(CodeScopes.Of(Scope.Violating).SourceDirectory), Cancellation));

    [Fact]
    public void AViolationNamesItsPlaceAndPromotesEventsAndTimeProviderTimers() =>
        Assert.Equal(
            $"{Fixture}:5: Thread.Sleep: Waits on time instead of an event. Await the event or signal that states the thing happened: an integration event, a component's refreshed signal, a TaskCompletionSource or a channel. For behavior that is genuinely about time, use a TimeProvider timer (TimeProvider.CreateTimer), testable with FakeTimeProvider.",
            new TimedWait(Fixture, 5, "Thread.Sleep").ToString());
}
