using System.Globalization;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Contracts;
using Avala.Forges.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Timeline;
using Avala.Workbench.Conversation;

namespace Avala.Workbench.Tests.Conversation;

public sealed class ConversationPhrasesTests
{
    [Theory]
    [InlineData(JobRejection.NotHeld, "The job takes a message only when it needs you or awaits review.")]
    [InlineData(JobRejection.NotRunning, "The job is not running.")]
    [InlineData(JobRejection.NotDiscardable, "The job already ended.")]
    [InlineData(JobRejection.EmptyMessage, "Write a message first.")]
    [InlineData(JobRejection.UnknownJob, "The job no longer exists.")]
    [InlineData(JobRejection.WorkspaceUnavailable, "The job's worktree is gone.")]
    [InlineData(JobRejection.UnknownConnection, "The job's connection cannot be used.")]
    [InlineData(JobRejection.UnusableConnection, "The job's connection cannot be used.")]
    [InlineData(JobRejection.AgentUnavailable, "The agent could not start.")]
    [InlineData(JobRejection.NotAwaitingReview, "The job no longer awaits review.")]
    [InlineData(JobRejection.NotSteerable, "This agent takes no message while it works.")]
    [InlineData(JobRejection.InvalidRequest, "The job refused the command.")]
    public void ARefusedCommandToAJobSaysWhy(JobRejection rejection, string phrase) =>
        Assert.Equal(phrase, ConversationPhrases.Rejection(rejection));

    [Theory]
    [InlineData(JobStatus.Running, true, "Message the agent while it works…", "Send into the running turn")]
    [InlineData(JobStatus.Running, false, "This agent takes no message mid-turn · queue one for when it stops…", "Queue for when the agent stops")]
    [InlineData(JobStatus.NeedsHelp, true, "Continue the job with a message…", "Send")]
    [InlineData(JobStatus.AwaitingReview, false, "Send the agent more to do before you review…", "Send")]
    [InlineData(JobStatus.Checking, true, "The checks are running · queue a message for when they end…", "Queue for when the agent stops")]
    [InlineData(JobStatus.Preparing, true, "The agent is starting · queue a message for when it stops…", "Queue for when the agent stops")]
    [InlineData(JobStatus.Draft, false, "The agent is starting · queue a message for when it stops…", "Queue for when the agent stops")]
    [InlineData(JobStatus.Approved, true, "This job has ended", "Send")]
    [InlineData(JobStatus.Discarded, false, "This job has ended", "Send")]
    [InlineData(JobStatus.Failed, true, "This job has ended", "Send")]
    public void TheComposerSaysWhatAMessageDoesForEachStatus(JobStatus status, bool takesMessagesMidTurn, string phrase, string hint) =>
        Assert.Equal((phrase, hint), (ConversationPhrases.Placeholder(status, takesMessagesMidTurn, Option<PullRequestWatchState>.None), ConversationPhrases.SendHint(status, takesMessagesMidTurn)));

    [Theory]
    [InlineData(OnPullRequest.WatchOnly, WatchStatus.Watching, "Approved · Avala watches pull request #7 until it is merged or closed")]
    [InlineData(OnPullRequest.WakeOnCi, WatchStatus.BackingOff, "Approved · Avala watches pull request #7 and wakes the agent if CI fails or it conflicts")]
    [InlineData(OnPullRequest.WakeOnCiAndReviews, WatchStatus.WaitingForJob, "Approved · Avala watches pull request #7 and wakes the agent if CI fails, it conflicts or a review asks for changes")]
    [InlineData(OnPullRequest.WakeOnCi, WatchStatus.NeedsPerson, "Approved · Avala stopped waking the agent: pull request #7 needs you")]
    [InlineData(OnPullRequest.WakeOnCi, WatchStatus.Ended, "This job has ended")]
    public void AnApprovedJobSaysWhatTheWatchOfItsPullRequestStillDoes(OnPullRequest policy, WatchStatus status, string phrase)
    {
        var watch = new PullRequestWatchState(JobId.New(), new ForgeName("github"), new PullRequestRef(7, new Uri("https://github.com/acme/shop/pull/7"), "avala/job", "main"), policy, 3) { Status = status };

        Assert.Equal(phrase, ConversationPhrases.Placeholder(JobStatus.Approved, false, watch));
    }

    [Theory]
    [InlineData(JobStatus.Running, "", "Running")]
    [InlineData(JobStatus.Checking, "", "Checking")]
    [InlineData(JobStatus.Preparing, "", "Starting")]
    [InlineData(JobStatus.AwaitingReview, "", "Ready for review")]
    [InlineData(JobStatus.Approved, "", "Approved")]
    [InlineData(JobStatus.Discarded, "", "Discarded")]
    [InlineData(JobStatus.Failed, "", "Failed")]
    [InlineData(JobStatus.NeedsHelp, "Stalled", "Held · stalled")]
    [InlineData(JobStatus.NeedsHelp, "BudgetExceeded", "Held · over budget")]
    [InlineData(JobStatus.NeedsHelp, "SessionLost", "Held · session lost")]
    [InlineData(JobStatus.NeedsHelp, "InvalidChecks", "Held · invalid checks")]
    public void TheStatusPillNamesWhereTheJobStands(JobStatus status, string hold, string phrase)
    {
        var job = new BoardJob(new FakeCatalog().Add("Fix JPY rounding in invoice totals", status).Summary, Transcript.Empty)
        {
            Hold = hold.Length == 0 ? Option<HoldReason>.None : Enum.Parse<HoldReason>(hold),
        };

        Assert.Equal(phrase, ConversationPhrases.Pill(job));
    }

    [Fact]
    public void TheStatusPillOfAJobWaitingForAResetSaysBrieflyUntilWhenItIsHeld()
    {
        var summary = new FakeCatalog().Add("Fix JPY rounding in invoice totals", JobStatus.NeedsHelp).Summary;
        var since = new DateTimeOffset(2026, 10, 10, 0, 20, 0, TimeSpan.Zero);
        var resumes = since.AddHours(2).AddMinutes(50);
        var later = since.AddDays(3);
        BoardJob Waiting(Option<DateTimeOffset> at) => new(summary, Transcript.Empty)
        {
            Hold = HoldReason.LimitNearlyReached,
            Wait = new ResetWait(summary.Job, new ConnectionName("claude-work"), "5h", at, since),
        };

        Assert.Equal($"Held · until {resumes.ToLocalTime():HH:mm}", ConversationPhrases.Pill(Waiting(resumes)));
        Assert.Equal($"Held · until {later.ToLocalTime().ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture)}", ConversationPhrases.Pill(Waiting(later)));
        Assert.Equal("Held · near its limit", ConversationPhrases.Pill(Waiting(Option<DateTimeOffset>.None)));
    }

    [Fact]
    public void TheStatusPillOfASubAgentWaitingForItsParentsAnswerSaysSo()
    {
        var session = SessionId.New();
        var turn = TurnId.New();
        var now = DateTimeOffset.UnixEpoch;
        var transcript = Transcript.Empty
            .Apply(new TurnStarted(session, turn), now)
            .Apply(new PermissionRequested(session, turn, new ItemId("migrate"), "Run a command", ItemKind.Command, "dotnet ef database update"), now)
            .Apply(new PolicyDecision(session, turn, new ItemId("migrate"), Option<JobId>.None, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToParent, now));

        Assert.Equal("Waiting for its parent", ConversationPhrases.Pill(new BoardJob(new FakeCatalog().Add("Migrate the database", JobStatus.Running).Summary, transcript)));
    }

    [Theory]
    [InlineData("/home/dev/code/billing-worker", "claude-work", "billing-worker · claude-work")]
    [InlineData("C:\\code\\ledger-api\\", "", "ledger-api")]
    public void TheJobIsPlacedByItsRepositoryAndConnection(string repository, string connection, string phrase)
    {
        var summary = new FakeCatalog().Add("Fix JPY rounding in invoice totals").Summary with
        {
            Repository = repository,
            Connection = connection.Length == 0 ? Option<ConnectionName>.None : new ConnectionName(connection),
        };

        Assert.Equal(phrase, ConversationPhrases.Place(summary));
    }
}
