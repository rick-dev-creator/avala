using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Presenting;
using Avala.Workbench.Tests.Following;

namespace Avala.Workbench.Tests.Board;

public sealed class BoardFeedTests : IDisposable
{
    private readonly JobBoard board = new();
    private readonly TestUiDispatcher ui = new();
    private readonly RecordingLogger<BoardFeed> logger = new();
    private readonly BoardFeed feed;

    public BoardFeedTests() => feed = new BoardFeed(board, ui, logger);

    [Fact]
    public async Task AShowThatThrowsIsLoggedAndTheBoardFeedKeepsFollowingAsync()
    {
        var shows = 0;
        feed.Start(_ => ++shows == 1 ? throw new InvalidOperationException("A row with no view") : []);
        await ui.PresentedAsync(feed, () => feed.Revision == 1, () => $"revision {feed.Revision}");

        board.Publish(Pages.Board(Pages.Summary("Fix the failing test", JobStatus.Running)).Jobs);

        await ui.PresentedAsync(feed, () => feed.Revision == 2, () => $"revision {feed.Revision}");
        Assert.Equal((2, 1), await ui.ReadAsync(() => (shows, logger.Entries.Count)));
    }

    [Fact]
    public async Task ALoadThatThrowsIsLoggedAndTheBoardFeedKeepsFollowingAsync()
    {
        var loads = 0;
        feed.Start(_ => [_ => Interlocked.Increment(ref loads) == 1 ? throw new IOException("The transcript went away") : Task.CompletedTask]);
        await ui.PresentedAsync(feed, () => feed.Revision == 1, () => $"revision {feed.Revision}");

        board.Publish(Pages.Board(Pages.Summary("Fix the failing test", JobStatus.Running)).Jobs);

        await ui.PresentedAsync(feed, () => feed.Revision == 2, () => $"revision {feed.Revision}");
        Assert.Single(logger.Entries);
    }

    [Fact]
    public async Task AShowAgainThatThrowsIsLoggedAndTheBoardFeedKeepsFollowingAsync()
    {
        feed.Start(_ => []);
        await ui.PresentedAsync(feed, () => feed.Revision == 1, () => $"revision {feed.Revision}");

        await feed.ShowAgainAsync(() => throw new InvalidOperationException("An aged row with no view"));
        board.Publish(Pages.Board(Pages.Summary("Fix the failing test", JobStatus.Running)).Jobs);

        await ui.PresentedAsync(feed, () => feed.Revision == 3, () => $"revision {feed.Revision}");
        Assert.Single(logger.Entries);
    }

    [Fact]
    public async Task ALoadOutsideTheFollowedChangesThatThrowsIsLoggedAsync()
    {
        feed.Start(_ => []);
        await ui.PresentedAsync(feed, () => feed.Revision == 1, () => $"revision {feed.Revision}");

        await feed.LoadAsync<bool>(_ => throw new IOException("The shelf file went away"), _ => { });

        Assert.Single(logger.Entries);
    }

    public void Dispose()
    {
        feed.Dispose();
        ui.Dispose();
    }
}
