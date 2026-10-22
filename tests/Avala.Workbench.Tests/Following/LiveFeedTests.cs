using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Workbench.Tests.Following;

public sealed class LiveFeedTests : IDisposable
{
    private readonly JobBoard board = new();
    private readonly TestUiDispatcher ui = new();
    private readonly RecordingLogger<LiveFeed> logger = new();
    private readonly Pulse pulse;
    private readonly LiveFeed feed;

    public LiveFeedTests()
    {
        pulse = new Pulse(board);
        feed = new LiveFeed(pulse, ui, logger);
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
    public async Task AReadThatThrowsIsReportedOnTheUiAndTheFeedKeepsFollowingAsync()
    {
        var reads = 0;
        var shown = new List<string>();
        feed.Start(
            _ => Interlocked.Increment(ref reads) == 1 ? throw new InvalidOperationException("A repeated section") : ValueTask.FromResult("read"),
            shown.Add,
            exception => shown.Add(exception.Message));
        await ui.PresentedAsync(feed, () => shown.Count == 1, () => $"{shown.Count} shown");

        await pulse.HandleAsync(new UsageRecorded(SessionId.New(), Avala.Sdk.Option<JobId>.None), CancellationToken.None);

        await ui.PresentedAsync(feed, () => shown.Count == 2, () => $"{shown.Count} shown");
        Assert.Equal(["A repeated section", "read"], await ui.ReadAsync(() => shown.ToList()));
    }

    [Fact]
    public async Task AStateQueuedForTheUiBeforeTheFeedStoppedIsNeverShownAsync()
    {
        var queue = new HeldDispatcher();
        using var held = new LiveFeed(pulse, queue, NullLogger<LiveFeed>.Instance);
        var shown = 0;
        held.Start(_ => ValueTask.FromResult(true), _ => shown++);
        var queued = await queue.Queued.Task.WaitAsync(TestContext.Current.CancellationToken);

        held.Stop();
        queued();
        await held.Following;

        Assert.Equal(0, shown);
    }

    [Fact]
    public async Task AFailedReadIsTheFeedsFailureUntilAReadSucceedsAgainAsync()
    {
        var reads = 0;
        feed.Start(_ => Interlocked.Increment(ref reads) == 1 ? throw new IOException("The usage store is locked") : ValueTask.FromResult(reads), _ => { });
        await ui.PresentedAsync(feed, () => feed.Revision == 1, () => $"revision {feed.Revision}");
        var failed = await ui.ReadAsync(() => feed.Failure);

        await pulse.HandleAsync(new UsageRecorded(SessionId.New(), Avala.Sdk.Option<JobId>.None), CancellationToken.None);

        await ui.PresentedAsync(feed, () => feed.Revision == 2, () => $"revision {feed.Revision}");
        Assert.Equal((Avala.Sdk.Option<string>.Some("The usage store is locked"), Avala.Sdk.Option<string>.None), (failed, await ui.ReadAsync(() => feed.Failure)));
    }

    [Fact]
    public async Task AFailureIsLoggedWithTheComponentItBelongsToEvenWithoutAFailCallbackAsync()
    {
        var shown = 0;
        feed.Start(_ => ValueTask.FromResult(0), state => shown = state == 0 ? throw new InvalidOperationException("State 0 has no view") : state);
        await ui.PresentedAsync(feed, () => logger.Entries.Count == 1, () => $"{logger.Entries.Count} logged");

        var (level, message) = Assert.Single(logger.Entries);
        Assert.Equal((LogLevel.Error, 0, "LiveFeedTests failed to read or show what it follows, and keeps following"), (level, shown, message));
    }

    [Fact]
    public async Task AFailCallbackThatThrowsDoesNotEndTheFeedAsync()
    {
        var reads = 0;
        var shown = new List<int>();
        feed.Start(
            _ => Interlocked.Increment(ref reads) == 1 ? throw new InvalidOperationException("A repeated section") : ValueTask.FromResult(reads),
            shown.Add,
            _ => throw new InvalidOperationException("The component failed to say so"));
        await ui.PresentedAsync(feed, () => feed.Revision == 1, () => $"revision {feed.Revision}");

        await pulse.HandleAsync(new UsageRecorded(SessionId.New(), Avala.Sdk.Option<JobId>.None), CancellationToken.None);

        await ui.PresentedAsync(feed, () => shown.Count == 1, () => $"{shown.Count} shown");
        Assert.Equal([2], await ui.ReadAsync(() => shown.ToList()));
    }

    [Fact]
    public async Task AFeedStoppedWhileItsReadFailsEndsQuietlyAsync()
    {
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failing = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new List<string>();
        feed.Start(
            async _ =>
            {
                reading.TrySetResult();

                return await failing.Task;
            },
            _ => { },
            exception => failed.Add(exception.Message));
        await reading.Task.WaitAsync(TestContext.Current.CancellationToken);
        var following = feed.Following;

        feed.Stop();
        failing.SetException(new IOException("The docs folder went away"));

        await following.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Empty(failed);
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
