using System.Threading.Channels;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Playback;
using Avala.Simulator.Scenarios;
using Avala.Testing;

namespace Avala.Simulator.Tests.Playback;

public sealed class QueuedAtTheEndTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMessageQueuedAfterTheTurnLastListenedStartsTheNextTurnAsTheRealHarnessDoesAsync()
    {
        var plain = await TurnLengthAsync();
        var clock = new HeldClock();
        using var data = new TemporaryFolder();
        using var folder = new TemporaryFolder();
        await using var session = new SimulatedSession(
            new SessionOptions(folder.Path, PermissionMode.AllowAll),
            Stage.Crafted(new AvalaPaths(data.Path)) with { Pacing = new Pacing(clock, TimeSpan.FromMilliseconds(1)) },
            SimulatedAccounts.Default,
            Option<Conversation>.None);
        var turn = Outcomes.Succeeds(await session.SendAsync(new UserTurn("[simulate: reply] Explain the rounding rules"), Cancellation));
        await using var events = session.Events.GetAsyncEnumerator(Cancellation);

        for (var published = 0; published < plain - 1; published++)
        {
            (await clock.Timers.Reader.ReadAsync(Cancellation))();
            Assert.True(await events.MoveNextAsync());
        }

        var last = await clock.Timers.Reader.ReadAsync(Cancellation);
        Assert.Equal(turn, Outcomes.Succeeds(await session.SendAsync(new UserTurn("Also cover the yen") { MidTurn = true }, Cancellation)));
        Assert.True(await events.MoveNextAsync());
        Assert.IsType<MessageQueued>(events.Current);
        last();
        Assert.True(await events.MoveNextAsync());
        var ended = events.Current;
        (await clock.Timers.Reader.ReadAsync(Cancellation).AsTask().WaitAsync(TimeSpan.FromSeconds(30), Cancellation))();
        Assert.True(await events.MoveNextAsync());

        Assert.Equal(new TurnCompleted(session.Id, turn, TurnOutcome.Finished), ended);
        Assert.IsType<TurnStarted>(events.Current);
    }

    private static async Task<int> TurnLengthAsync()
    {
        await using var stage = new Stage(PermissionMode.AllowAll);
        await stage.SendAsync("[simulate: reply] Explain the rounding rules", Cancellation);

        return (await stage.ReadTurnAsync(Cancellation)).Count;
    }

    private sealed class HeldClock : TimeProvider
    {
        public Channel<Action> Timers { get; } = Channel.CreateUnbounded<Action>();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Timers.Writer.TryWrite(() => callback(state));

            return new Held();
        }

        private sealed class Held : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
