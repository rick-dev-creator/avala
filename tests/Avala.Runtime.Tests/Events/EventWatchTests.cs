using System.Threading.Channels;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Runtime.Tests.Events;

public sealed class EventWatchTests
{
    private static readonly ProcessRequest Git = new("git", ["worktree", "add"]);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AWatchGivesUpOnceNothingHappensForThirtySecondsAsync()
    {
        var clock = new FakeTimeProvider();
        var events = Channel.CreateUnbounded<Pinged>();
        var watch = new EventWatch<Pinged>(events.Reader.ReadAllAsync(Cancellation), Cancellation) { Clock = clock, Processes = new ProcessesInFlight(clock) };

        var until = watch.UntilAsync(_ => true);
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Contains("within 30 seconds of the last step or process", (await Assert.ThrowsAsync<TimeoutException>(() => until)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWatchKeepsWaitingWhileAProcessOfTheApplicationRunsAsync()
    {
        var clock = new FakeTimeProvider();
        var events = Channel.CreateUnbounded<Pinged>();
        var processes = new ProcessesInFlight(clock);
        var git = new HeldProcess();
        var running = processes.Counting(git).RunAsync(Git, Cancellation);
        var watch = new EventWatch<Pinged>(events.Reader.ReadAllAsync(Cancellation), Cancellation) { Clock = clock, Processes = processes };

        var until = watch.UntilAsync(_ => true);
        clock.Advance(TimeSpan.FromSeconds(45));
        git.Finish();
        await running;
        Assert.True(events.Writer.TryWrite(new Pinged()));

        Assert.Equal(new Pinged(), await until);
    }

    [Fact]
    public async Task AWatchGivesUpAfterFiveMinutesEvenWhileAProcessKeepsRunningAsync()
    {
        var clock = new FakeTimeProvider();
        var events = Channel.CreateUnbounded<Pinged>();
        var processes = new ProcessesInFlight(clock);
        var running = processes.Counting(new HeldProcess()).RunAsync(Git, Cancellation).AsTask();
        var watch = new EventWatch<Pinged>(events.Reader.ReadAllAsync(Cancellation), Cancellation) { Clock = clock, Processes = processes };

        var until = watch.UntilAsync(_ => true);
        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Contains("in the 5 minutes a process kept running", (await Assert.ThrowsAsync<TimeoutException>(() => until)).Message, StringComparison.Ordinal);
        Assert.False(running.IsCompleted);
    }

    private sealed record Pinged : IIntegrationEvent;

    private sealed class HeldProcess : IProcessRunner
    {
        private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Finish() => finished.SetResult();

        public async ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            await finished.Task.WaitAsync(cancellationToken);

            return new ProcessOutcome(0, string.Empty, string.Empty);
        }
    }
}
