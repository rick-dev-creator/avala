using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Drafting;
using Avala.Workbench.Drafting.Storage;
using Avala.Workbench.Mentioning;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Drafting;

public sealed class DraftBookTests
{
    private static readonly JobId Job = new(Guid.Parse("0c4f0a8e-62a1-4f37-9f61-7c1a1d9b8e10"));

    private static readonly Draft Written = new(
        "Round the yen once",
        [new Attachment("screen.png", "image/png", "/data/attachments/screen.png")],
        [new Mention(MentionKind.File, "src/Rounding.cs", "Rounding.cs", "src", "the file src/Rounding.cs in the job's worktree")]);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADraftIsMarkedOnTheBoardAtOnceAndWrittenOnceTypingSettlesAsync()
    {
        var (book, store, board, clock) = Open();

        book.Keep(Job, Written);
        var before = store.Writes;
        clock.Advance(DraftBook.Settle);
        await book.Written;

        Assert.Equal((0, true), (before, board.Drafted.Contains(Job)));
        Assert.Equal(Written, store.Kept[Job]);
    }

    [Fact]
    public async Task ADraftLeftEmptyIsForgottenAndUnmarkedAsync()
    {
        var (book, store, board, clock) = Open();
        book.Keep(Job, Written);

        book.Keep(Job, Draft.Empty with { Text = "  " });
        clock.Advance(DraftBook.Settle);
        await book.Written;

        Assert.Equal((false, false), (board.Drafted.Contains(Job), store.Kept.ContainsKey(Job)));
        Assert.True(book.Of(Job).IsNone);
    }

    [Fact]
    public async Task ClosingTheBookWritesTheDraftStillWaitingToSettleAsync()
    {
        var (book, store, _, _) = Open();
        book.Keep(Job, Written);

        await book.DisposeAsync();

        Assert.Equal(Written, store.Kept[Job]);
    }

    [Fact]
    public async Task TheDraftsOfAnEarlierRunAreRestoredAndMarkedAtStartupAsync()
    {
        var (book, store, board, _) = Open();
        store.Kept[Job] = Written;

        await book.RunAsync(Cancellation);

        Assert.Equal((Option<Draft>.Some(Written), true), (book.Of(Job), board.Drafted.Contains(Job)));
    }

    [Fact]
    public async Task TheDraftFileKeepsTheTextTheAttachmentsAndTheMentionsAcrossARestartAsync()
    {
        await using var folder = new TemporaryFolder();
        var file = new DraftFile(new AvalaPaths(folder.Path));

        await file.WriteAsync(new Dictionary<JobId, Draft> { [Job] = Written }, Cancellation);
        var read = await new DraftFile(new AvalaPaths(folder.Path)).ReadAsync(Cancellation);

        Assert.Equal(Written.Text, read[Job].Text);
        Assert.Equal(Written.Attachments, read[Job].Attachments);
        Assert.Equal(Written.Mentions, read[Job].Mentions);
    }

    [Fact]
    public async Task AMessageStillQueuedWhenAvalaStoppedComesBackAsReturnedAtStartupAsync()
    {
        await using var folder = new TemporaryFolder();
        var waiting = new WaitingMessage(MessageId.New(), "Keep the alias", [], false);
        await new DraftFile(new AvalaPaths(folder.Path)).WriteAsync(new Dictionary<JobId, Draft> { [Job] = Draft.Empty with { Waiting = [waiting] } }, Cancellation);
        var (board, clock) = (new JobBoard(), new FakeTimeProvider());
        var book = new DraftBook(new DraftFile(new AvalaPaths(folder.Path)), board, clock);

        await book.RunAsync(Cancellation);

        Assert.Contains(Job, board.Drafted);
        Assert.Equal([(waiting.Id, waiting.Text, true)], book.TakeReturned(Job).Select(message => (message.Id, message.Text, message.Returned)));
    }

    [Fact]
    public async Task AnUnreadableDraftFileStartsWithNoDraftsAsync()
    {
        await using var folder = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(folder.Path, DraftFile.FileName), "{ not json", Cancellation);

        Assert.Empty(await new DraftFile(new AvalaPaths(folder.Path)).ReadAsync(Cancellation));
    }

    private static (DraftBook Book, FakeDraftStore Store, JobBoard Board, FakeTimeProvider Clock) Open()
    {
        var store = new FakeDraftStore();
        var board = new JobBoard();
        var clock = new FakeTimeProvider();

        return (new DraftBook(store, board, clock), store, board, clock);
    }
}
