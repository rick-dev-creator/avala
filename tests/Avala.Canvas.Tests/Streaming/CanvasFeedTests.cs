using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Canvas.Gallery;
using Avala.Canvas.Streaming;
using Avala.Canvas.Throttling;
using Avala.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Canvas.Tests.Streaming;

public sealed class CanvasFeedTests : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private readonly FakeTimeProvider clock = new();
    private readonly RecordingBus bus = new();
    private readonly CanvasGallery gallery = new();
    private readonly SnapshotThrottle throttle;
    private readonly CanvasFeed feed;
    private readonly Sketch sketch = Sketch.New();

    public CanvasFeedTests()
    {
        throttle = new SnapshotThrottle(gallery, bus, clock, Interval);
        feed = new CanvasFeed(gallery, throttle, NullLogger<CanvasFeed>.Instance);
    }

    private IReadOnlyList<CanvasSnapshot> Published => [.. bus.Published.OfType<CanvasUpdated>().Select(update => update.Snapshot)];

    [Fact]
    public async Task AStartedCanvasIsPublishedAtOnceEmptyAndStreamingAsync()
    {
        await FeedAsync(sketch.Started());

        Assert.Equal(
            new CanvasSnapshot(sketch.Id, sketch.Session, "Architecture", "image/svg+xml", string.Empty, CanvasStatus.Streaming),
            Assert.Single(Published));
    }

    [Fact]
    public async Task ChunksWithinTheIntervalArePublishedOnceTogetherWhenItElapsesAsync()
    {
        await FeedAsync(sketch.Started(), sketch.Chunk("<svg>"), sketch.Chunk("<rect/>"));
        await AdvanceAsync(Interval - TimeSpan.FromMilliseconds(1));

        Assert.Single(Published);

        await AdvanceAsync(TimeSpan.FromMilliseconds(1));

        Assert.Equal([string.Empty, "<svg><rect/>"], Published.Select(snapshot => snapshot.Content));
    }

    [Fact]
    public async Task AChunkAfterAQuietIntervalIsPublishedAtOnceAsync()
    {
        await FeedAsync(sketch.Started());
        await AdvanceAsync(Interval);

        await FeedAsync(sketch.Chunk("<svg>"));

        Assert.Equal([string.Empty, "<svg>"], Published.Select(snapshot => snapshot.Content));
    }

    [Theory]
    [InlineData(ItemOutcome.Succeeded, CanvasStatus.Completed)]
    [InlineData(ItemOutcome.Failed, CanvasStatus.Failed)]
    [InlineData(ItemOutcome.Cancelled, CanvasStatus.Cancelled)]
    [InlineData(ItemOutcome.Abandoned, CanvasStatus.Abandoned)]
    [InlineData(ItemOutcome.Expired, CanvasStatus.Expired)]
    public async Task CompletionPublishesTheFinalStateAtOnceAndNothingAfterItAsync(ItemOutcome outcome, CanvasStatus status)
    {
        await FeedAsync(sketch.Started(), sketch.Chunk("<svg>"), sketch.Chunk("</svg>"), sketch.Completed(outcome));
        await AdvanceAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            [(string.Empty, CanvasStatus.Streaming), ("<svg></svg>", status)],
            Published.Select(snapshot => (snapshot.Content, snapshot.Status)));
    }

    [Fact]
    public async Task AFlushDueAfterTheCanvasClosedLeavesTheFinalSnapshotToCompletionAsync()
    {
        await FeedAsync(sketch.Started(), sketch.Chunk("<svg/>"));
        _ = gallery.Close(sketch.Completed());

        await AdvanceAsync(Interval);

        Assert.Single(Published);
    }

    [Fact]
    public async Task ActivityOtherThanCanvasesPublishesNothingAsync()
    {
        var reply = new ItemId("reply");

        await FeedAsync(
            new ItemStarted(sketch.Session, sketch.Turn, reply, ItemKind.Message, "Reply"),
            new ItemProgressed(sketch.Session, sketch.Turn, reply, "Hello"),
            new ItemCompleted(sketch.Session, sketch.Turn, reply, ItemOutcome.Succeeded),
            new TurnCompleted(sketch.Session, sketch.Turn, TurnOutcome.Finished));
        await AdvanceAsync(TimeSpan.FromSeconds(1));

        Assert.Empty(Published);
    }

    [Fact]
    public async Task RejectedCanvasContentPublishesNothingAsync()
    {
        await FeedAsync(sketch.Started(), sketch.Completed());

        await FeedAsync(sketch.Started(), sketch.Chunk("<late/>"), sketch.Completed());
        await AdvanceAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(2, Published.Count);
    }

    public async ValueTask DisposeAsync() => await throttle.DisposeAsync();

    private async Task AdvanceAsync(TimeSpan elapsed)
    {
        clock.Advance(elapsed);
        await throttle.IdleAsync(TestContext.Current.CancellationToken);
    }

    private async Task FeedAsync(params IAgentEvent[] events)
    {
        foreach (var agentEvent in events)
        {
            await feed.HandleAsync(new AgentActivity(agentEvent), TestContext.Current.CancellationToken);
        }
    }
}
