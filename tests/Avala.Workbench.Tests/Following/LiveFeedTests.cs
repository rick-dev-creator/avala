using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;

namespace Avala.Workbench.Tests.Following;

public sealed class LiveFeedTests : IDisposable
{
    private readonly JobBoard board = new();
    private readonly TestUiDispatcher ui = new();
    private readonly Pulse pulse;
    private readonly LiveFeed feed;

    public LiveFeedTests()
    {
        pulse = new Pulse(board);
        feed = new LiveFeed(pulse, ui);
    }

    [Fact]
    public async Task AStartedFeedShowsItsStateAtOnceAndAgainOnEveryFollowedEventAsync()
    {
        var reads = 0;
        var shown = new List<int>();
        feed.Start(_ => ValueTask.FromResult(Interlocked.Increment(ref reads)), shown.Add);
        await ui.PresentedAsync(feed, () => shown.Count == 1, () => $"{shown.Count} shown");

        await pulse.HandleAsync(new UsageRecorded(SessionId.New(), Avala.Sdk.Option<JobId>.None), CancellationToken.None);

        await ui.PresentedAsync(feed, () => shown.Count == 2, () => $"{shown.Count} shown");
        Assert.Equal([1, 2], await ui.ReadAsync(() => shown.ToList()));
    }

    [Fact]
    public async Task AChangeOfTheBoardWakesTheFeedAsync()
    {
        var shown = 0;
        feed.Start(_ => ValueTask.FromResult(true), _ => shown++);
        await ui.PresentedAsync(feed, () => shown == 1, () => $"{shown} shown");

        board.Publish(Pages.Board(Pages.Summary("Fix the failing test", JobStatus.Running)).Jobs);

        await ui.PresentedAsync(feed, () => shown == 2, () => $"{shown} shown");
        Assert.Equal(2, await ui.ReadAsync(() => shown));
    }

    [Fact]
    public async Task AStateQueuedForTheUiBeforeTheFeedStoppedIsNeverShownAsync()
    {
        var queue = new HeldDispatcher();
        using var held = new LiveFeed(pulse, queue);
        var shown = 0;
        held.Start(_ => ValueTask.FromResult(true), _ => shown++);
        var queued = await queue.Queued.Task.WaitAsync(TestContext.Current.CancellationToken);

        held.Stop();
        queued();
        await held.Following;

        Assert.Equal(0, shown);
    }

    public void Dispose()
    {
        feed.Dispose();
        ui.Dispose();
    }

    private sealed class HeldDispatcher : Avala.Sdk.IUiDispatcher
    {
        public TaskCompletionSource<Action> Queued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken)
        {
            Queued.TrySetResult(action);

            return ValueTask.CompletedTask;
        }
    }
}
