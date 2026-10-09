using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workbench.Board;
using Avala.Workbench.Timeline;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Board;

public sealed class BoardKeeperTests
{
    private readonly FakeCatalog catalog = new();
    private readonly JobBoard board = new();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero));
    private readonly BoardKeeper keeper;

    public BoardKeeperTests() => keeper = new BoardKeeper(catalog, board, time);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASubmittedJobJoinsWithItsInstructionAsTheFirstPrompt()
    {
        var job = catalog.Add("Fix the failing test").Summary.Job;

        await keeper.HandleAsync(new JobSubmitted(job), Cancellation);

        var joined = Joined(job);
        var prompt = Assert.IsType<PromptEntry>(Assert.Single(joined.Transcript.Entries));
        Assert.Equal(("Fix the failing test", JobStatus.Preparing), (prompt.Text.Match(text => text, () => string.Empty), joined.Status));
    }

    [Fact]
    public async Task ActivityLandsInTheTranscriptOfItsSessionsJobAndOtherSessionsAreIgnored()
    {
        var job = catalog.Add("Fix the failing test").Summary.Job;
        var session = SessionId.New();
        var turn = TurnId.New();
        await keeper.HandleAsync(new JobSubmitted(job), Cancellation);
        await keeper.HandleAsync(new JobSessionStarted(job, session), Cancellation);

        await keeper.HandleAsync(new AgentActivity(new TurnStarted(session, turn)), Cancellation);
        await keeper.HandleAsync(new AgentActivity(new PermissionRequested(session, turn, new ItemId("migrate"), "Run a command", ItemKind.Command, "dotnet ef database update")), Cancellation);
        await keeper.HandleAsync(new PermissionDecided(new PolicyDecision(session, turn, new ItemId("migrate"), job, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, time.GetUtcNow())), Cancellation);
        await keeper.HandleAsync(new AgentActivity(new ItemStarted(SessionId.New(), turn, new ItemId("other"), ItemKind.Message, "Reply")), Cancellation);

        var joined = Joined(job);
        Assert.Equal([typeof(PromptEntry), typeof(PermissionEntry)], joined.Transcript.Entries.Select(entry => entry.GetType()));
        Assert.Equal(1, joined.PendingDecisions);
    }

    [Fact]
    public async Task ProgressSetsTheStatusRefreshesThePromptsAndAHoldLastsUntilTheJobRunsAgain()
    {
        var job = catalog.Add("Fix the failing test").Summary.Job;
        await keeper.HandleAsync(new JobSubmitted(job), Cancellation);
        catalog.Attempted(job, Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Interrupted));
        await keeper.HandleAsync(new JobProgressed(job, JobStatus.NeedsHelp), Cancellation);
        await keeper.HandleAsync(new JobHeld(new JobHold(job, SessionId.New(), HoldReason.Stalled, SessionHalt.Interrupted)), Cancellation);
        var held = Joined(job);

        catalog.Attempted(job, Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Interrupted), Attempt(2, AttemptOrigin.Hint, AttemptOutcome.Running, "Use the staging database"));
        await keeper.HandleAsync(new JobProgressed(job, JobStatus.Running), Cancellation);
        var resumed = Joined(job);

        Assert.Equal((JobStatus.NeedsHelp, Option<HoldReason>.Some(HoldReason.Stalled)), (held.Status, held.Hold));
        Assert.Equal((JobStatus.Running, Option<HoldReason>.None, 2), (resumed.Status, resumed.Hold, resumed.Attempts));
        Assert.Equal(
            ["Fix the failing test", "Use the staging database"],
            resumed.Transcript.Entries.Cast<PromptEntry>().Select(prompt => prompt.Text.Match(text => text, () => string.Empty)));
    }

    [Fact]
    public async Task AtStartupTheJobsOfEarlierRunsJoinWithAMarkAfterTheirAttempts()
    {
        var earlier = catalog.Add("Update the dependency", JobStatus.AwaitingReview, Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Passed)).Summary.Job;
        var live = catalog.Add("Fix the failing test").Summary.Job;
        await keeper.HandleAsync(new JobSubmitted(live), Cancellation);

        await keeper.HandleAsync(new StartupCompleted(), Cancellation);

        Assert.Equal([typeof(PromptEntry), typeof(RestartEntry)], Joined(earlier).Transcript.Entries.Select(entry => entry.GetType()));
        Assert.Equal([typeof(PromptEntry)], Joined(live).Transcript.Entries.Select(entry => entry.GetType()));
    }

    [Fact]
    public async Task ATurnIsTimedWithTheTimeProvider()
    {
        var job = catalog.Add("Fix the failing test").Summary.Job;
        var session = SessionId.New();
        var turn = TurnId.New();
        await keeper.HandleAsync(new JobSessionStarted(job, session), Cancellation);
        await keeper.HandleAsync(new AgentActivity(new TurnStarted(session, turn)), Cancellation);
        time.Advance(TimeSpan.FromSeconds(7));

        await keeper.HandleAsync(new AgentActivity(new TurnCompleted(session, turn, TurnOutcome.Finished)), Cancellation);

        Assert.Equal(TimeSpan.FromSeconds(7), Assert.IsType<TurnEndEntry>(Joined(job).Transcript.Entries[^1]).Duration);
    }

    private BoardJob Joined(JobId job) => board.Find(job).Match(found => found, () => throw new InvalidOperationException("The job is not on the board."));

    private static AttemptRecord Attempt(int number, AttemptOrigin origin, AttemptOutcome outcome, string guidance = "") =>
        new(number, origin, outcome, string.IsNullOrEmpty(guidance) ? Option<string>.None : guidance, Option<SessionId>.None);
}
