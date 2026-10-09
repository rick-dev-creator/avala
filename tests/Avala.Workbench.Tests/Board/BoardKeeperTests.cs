using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Observability.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Transcripts.Contracts;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;
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

    private readonly FakeAudit audit = new();

    private readonly FakeTranscripts transcripts = new();

    public BoardKeeperTests() => keeper = new BoardKeeper(catalog, new BoardJoiner(audit, transcripts), board, time);

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AJobKnowsWhetherTheSessionItRunsInTakesMessagesMidTurn(bool declared)
    {
        var job = catalog.Add("Rename the orders module").Summary.Job;
        var session = SessionId.New();
        var capabilities = declared ? CapabilitySet.Of(new AcceptsMessagesMidTurn()) : CapabilitySet.None;
        await keeper.HandleAsync(new JobSubmitted(job), Cancellation);

        await keeper.HandleAsync(new SessionOpened(session, new ProviderInfo("simulator", "Simulator"), ".", new ConnectionName("claude-work")) { Capabilities = capabilities }, Cancellation);
        await keeper.HandleAsync(new JobSessionStarted(job, session), Cancellation);

        Assert.Equal(declared, Joined(job).TakesMessagesMidTurn);
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
    public async Task AJobThatStartsShowsTheConnectionItsSessionOpenedOnAsync()
    {
        var job = catalog.Add("Fix the failing test").Summary.Job;
        await keeper.HandleAsync(new JobSubmitted(job), Cancellation);
        var submitted = Joined(job).Summary.Connection;
        catalog.Change(job, history => history with { Summary = history.Summary with { Connection = new ConnectionName("claude-personal"), Status = JobStatus.Running } });

        await keeper.HandleAsync(new JobSessionStarted(job, SessionId.New()), Cancellation);

        var started = Joined(job);
        Assert.Equal((true, Option<ConnectionName>.Some(new ConnectionName("claude-personal")), JobStatus.Preparing), (submitted.IsNone, started.Summary.Connection, started.Status));
    }

    [Fact]
    public async Task AConnectionChosenByCapacityStaysWithItsJobAndAJobUnknownSoFarJoinsWithItAsync()
    {
        var job = catalog.Add("Fix the failing test").Summary.Job;
        var choice = new ConnectionChoice(new ConnectionName("claude-personal"), ChoiceReason.MostCapacity, [], time.GetUtcNow());

        await keeper.HandleAsync(new ConnectionChosen(job, choice), Cancellation);

        Assert.Equal(Option<ConnectionChoice>.Some(choice), Joined(job).Choice);
    }

    [Fact]
    public async Task AtStartupTheJobsOfEarlierRunsJoinWithAMarkAfterTheirAttemptsTheirLastStoredVerificationAndTheirStoredConnectionChoice()
    {
        var earlier = catalog.Add("Update the dependency", JobStatus.AwaitingReview, Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Passed)).Summary.Job;
        var choice = new ConnectionChoice(new ConnectionName("personal"), ChoiceReason.MostCapacity, [], time.GetUtcNow());
        catalog.Change(earlier, history => history with { Choice = choice });
        var live = catalog.Add("Fix the failing test").Summary.Job;
        var verified = new VerificationReport(earlier, 2, VerificationOutcome.Passed, Option<FileOrigin>.None, [], GateVerdict.Pass, time.GetUtcNow());
        audit.Reports.AddRange([verified with { Attempt = 1, Outcome = VerificationOutcome.Failed }, verified]);
        await keeper.HandleAsync(new JobSubmitted(live), Cancellation);

        await keeper.HandleAsync(new StartupCompleted(), Cancellation);

        Assert.Equal([typeof(PromptEntry), typeof(RestartEntry)], Joined(earlier).Transcript.Entries.Select(entry => entry.GetType()));
        Assert.Equal(Option<VerificationReport>.Some(verified), Joined(earlier).Verification);
        Assert.Equal(Option<ConnectionChoice>.Some(choice), Joined(earlier).Choice);
        Assert.Equal([typeof(PromptEntry)], Joined(live).Transcript.Entries.Select(entry => entry.GetType()));
    }

    [Fact]
    public async Task AJobOfAnEarlierRunJoinsWithTheConversationItsTranscriptKeptThenTheRestartMark()
    {
        var session = SessionId.New();
        var turn = TurnId.New();
        var earlier = catalog.Add("Update the dependency", JobStatus.AwaitingReview, Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Passed)).Summary.Job;
        var run = Guid.CreateVersion7();
        transcripts.Kept[earlier] =
        [
            new KeptFact(run, time.GetUtcNow(), new AttemptBegan(1)),
            new KeptFact(run, time.GetUtcNow(), new AgentActed(new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Reply"))),
            new KeptFact(run, time.GetUtcNow(), new AgentActed(new ItemProgressed(session, turn, new ItemId("reply"), "Bumped it."))),
        ];

        await keeper.HandleAsync(new StartupCompleted(), Cancellation);

        var entries = Joined(earlier).Transcript.Entries;
        Assert.Equal([typeof(PromptEntry), typeof(MessageEntry), typeof(RestartEntry)], entries.Select(entry => entry.GetType()));
        Assert.Equal(("Bumped it.", true), (((MessageEntry)entries[1]).Text, ((RestartEntry)entries[2]).Kept));
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

    [Fact]
    public async Task TheRevisionMovesOnEveryEventTheReviewAndTheInspectorReadButNotOnStreamedText()
    {
        var parent = catalog.Add("Split the work").Summary.Job;
        var child = catalog.Add("Write the notes").Summary.Job;
        var session = SessionId.New();
        var turn = TurnId.New();
        await keeper.HandleAsync(new JobSubmitted(parent), Cancellation);
        await keeper.HandleAsync(new JobSubmitted(child), Cancellation);
        await keeper.HandleAsync(new JobSessionStarted(parent, session), Cancellation);
        await keeper.HandleAsync(new AgentActivity(new TurnStarted(session, turn)), Cancellation);
        await keeper.HandleAsync(new AgentActivity(new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Reply")), Cancellation);
        var started = (Joined(parent).Revision, Joined(child).Revision);

        await keeper.HandleAsync(new AgentActivity(new ItemProgressed(session, turn, new ItemId("reply"), "Working on it")), Cancellation);
        var streamed = Joined(parent).Revision;
        await keeper.HandleAsync(new UsageRecorded(session, parent), Cancellation);
        await keeper.HandleAsync(new BudgetCarved(new BudgetCarve(parent, child, [], Option<long>.None, 0.5, time.GetUtcNow())), Cancellation);
        await keeper.HandleAsync(new PermissionAnswered(new HumanAnswer(session, parent, new ItemId("edit"), ItemKind.FileEdit, "NOTES.md", PermissionAnswer.Allow, Option<string>.None, Option<PolicyRule>.None, time.GetUtcNow())), Cancellation);

        Assert.Equal((1, 0), started);
        Assert.Equal(1, streamed);
        Assert.Equal((4, 1), (Joined(parent).Revision, Joined(child).Revision));
    }

    [Fact]
    public async Task ADelegationMovesTheRevisionOfEachJobItNamesAndOnlyThose()
    {
        var parent = catalog.Add("Split CheckoutPage into steps").Summary.Job;
        var child = catalog.Add("Extract the address step").Summary.Job;
        await keeper.HandleAsync(new JobSubmitted(parent), Cancellation);
        await keeper.HandleAsync(new JobSubmitted(child), Cancellation);
        var record = new Avala.Delegation.Contracts.DelegationRecord(SessionId.New(), new ItemId("delegate"), "Extract the address step", time.GetUtcNow());

        await keeper.HandleAsync(new Avala.Delegation.Contracts.ChildDelegated(record with { Parent = parent }), Cancellation);
        var refused = (Joined(parent).Revision, Joined(child).Revision);
        await keeper.HandleAsync(new Avala.Delegation.Contracts.ChildReported(record with { Parent = parent, Child = child }), Cancellation);
        await keeper.HandleAsync(new Avala.Delegation.Contracts.ChildDelegated(record), Cancellation);

        Assert.Equal((1, 0), refused);
        Assert.Equal((2, 1), (Joined(parent).Revision, Joined(child).Revision));
    }

    private BoardJob Joined(JobId job) => board.Find(job).Match(found => found, () => throw new InvalidOperationException("The job is not on the board."));

    private static AttemptRecord Attempt(int number, AttemptOrigin origin, AttemptOutcome outcome, string guidance = "") =>
        new(number, origin, outcome, string.IsNullOrEmpty(guidance) ? Option<string>.None : guidance, Option<SessionId>.None);
}
