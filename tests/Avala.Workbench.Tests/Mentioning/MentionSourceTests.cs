using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Mentioning;
using Avala.Workbench.Timeline;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Mentioning;

public sealed class MentionSourceTests
{
    private readonly Worktree worktree = new();
    private readonly FakeRepositoryIndex index = new();
    private readonly JobBoard board = new();
    private readonly FakeTimeProvider clock = new(DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task FilesFoldersCommitsAndOtherJobsAreSuggestedWithNamesThatStartWithTheQueryFirstAsync()
    {
        index.Files.AddRange(["docs/rounding.md", "src/Checkout/Rounding.cs", "src/Checkout/Prices.cs"]);
        index.Commits.Add(new CommitSummary("a3f9c21", "Round prices to the minor unit"));
        var source = Source();
        Board(Job("Explain the rounding rules", "Banker's rounding."));

        var found = await source.SuggestAsync(worktree.Job, "round", CancellationToken.None);

        Assert.Equal(
            [(MentionKind.File, "docs/rounding.md"), (MentionKind.File, "src/Checkout/Rounding.cs"), (MentionKind.Commit, "a3f9c21"), (MentionKind.Job, "Explain the rounding rules")],
            found.Select(mention => (mention.Kind, mention.Label)));
        Assert.Contains(new Mention(MentionKind.Folder, "src/Checkout/", "src/Checkout/", string.Empty, "the folder src/Checkout/ in the job's worktree"), await source.SuggestAsync(worktree.Job, "check", CancellationToken.None));
    }

    [Fact]
    public async Task NoMoreThanEightSuggestionsAreShownAsync()
    {
        index.Files.AddRange(Enumerable.Range(1, 20).Select(number => $"note{number}.md"));

        var found = await Source().SuggestAsync(worktree.Job, "note", CancellationToken.None);

        Assert.Equal(MentionSource.Shown, found.Count);
    }

    [Fact]
    public async Task TheRepositoryIsListedAgainOnlyAfterHalfAMinuteAsync()
    {
        var source = Source();

        await source.SuggestAsync(worktree.Job, "a", CancellationToken.None);
        await source.SuggestAsync(worktree.Job, "ab", CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(31));
        await source.SuggestAsync(worktree.Job, "abc", CancellationToken.None);

        Assert.Equal(2, index.Listings);
    }

    [Fact]
    public async Task AnotherJobIsReferencedByItsInstructionStatusAndLastReplyAsync()
    {
        var source = Source();
        Board(Job("Explain the rounding rules", "Banker's rounding, to the even digit."));

        var job = Assert.Single(await source.SuggestAsync(worktree.Job, "explain", CancellationToken.None));

        Assert.Equal("another job, \"Explain the rounding rules\", AwaitingReview: Explain the rounding rules\nIts last reply: Banker's rounding, to the even digit.", job.Reference);
    }

    private MentionSource Source() => new(worktree.Worked(), index, board, clock);

    private void Board(params BoardJob[] jobs) =>
        board.Publish(jobs.ToImmutableDictionary(job => job.Job, job => job));

    private static BoardJob Job(string instruction, string reply)
    {
        var session = SessionId.New();
        var turn = TurnId.New();
        var summary = new FakeCatalog().Add(instruction, JobStatus.AwaitingReview).Summary;
        var transcript = Transcript.Empty.WithPrompts(instruction, [])
            .Apply(new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Reply"), DateTimeOffset.UnixEpoch)
            .Apply(new ItemProgressed(session, turn, new ItemId("reply"), reply), DateTimeOffset.UnixEpoch);

        return new BoardJob(summary, transcript);
    }
}
