using Avala.Agents.Contracts.Connections;
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
    [InlineData(JobStatus.Running, true, "Message the agent while it works…", "Send into the running turn (Ctrl+Enter)")]
    [InlineData(JobStatus.Running, false, "This agent takes no message mid-turn · queue one for when it stops…", "Queue for when the agent stops (Ctrl+Enter)")]
    [InlineData(JobStatus.NeedsHelp, true, "Continue the job with a message…", "Send (Ctrl+Enter)")]
    [InlineData(JobStatus.AwaitingReview, false, "Send the agent more to do before you review…", "Send (Ctrl+Enter)")]
    [InlineData(JobStatus.Checking, true, "The checks are running · queue a message for when they end…", "Queue for when the agent stops (Ctrl+Enter)")]
    [InlineData(JobStatus.Preparing, true, "The agent is starting · queue a message for when it stops…", "Queue for when the agent stops (Ctrl+Enter)")]
    [InlineData(JobStatus.Draft, false, "The agent is starting · queue a message for when it stops…", "Queue for when the agent stops (Ctrl+Enter)")]
    [InlineData(JobStatus.Approved, true, "This job has ended", "Send (Ctrl+Enter)")]
    [InlineData(JobStatus.Discarded, false, "This job has ended", "Send (Ctrl+Enter)")]
    [InlineData(JobStatus.Failed, true, "This job has ended", "Send (Ctrl+Enter)")]
    public void TheComposerSaysWhatAMessageDoesForEachStatus(JobStatus status, bool takesMessagesMidTurn, string phrase, string hint) =>
        Assert.Equal((phrase, hint), (ConversationPhrases.Placeholder(status, takesMessagesMidTurn), ConversationPhrases.SendHint(status, takesMessagesMidTurn)));

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
    public void TheStatusPillNamesWhereTheJobStands(JobStatus status, string hold, string phrase)
    {
        var job = new BoardJob(new FakeCatalog().Add("Fix JPY rounding in invoice totals", status).Summary, Transcript.Empty)
        {
            Hold = hold.Length == 0 ? Option<HoldReason>.None : Enum.Parse<HoldReason>(hold),
        };

        Assert.Equal(phrase, ConversationPhrases.Pill(job));
    }

    [Fact]
    public void TheStatusPillOfAJobWaitingForAResetSaysWhenItResumes()
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

        Assert.Equal($"Held · resumes at {resumes.ToLocalTime():HH:mm} when the 5-hour window resets", ConversationPhrases.Pill(Waiting(resumes)));
        Assert.Equal($"Held · resumes at {later.ToLocalTime():yyyy-MM-dd HH:mm} when the 5-hour window resets", ConversationPhrases.Pill(Waiting(later)));
        Assert.Equal("Held · near its limit · no reset time reported, waits for you", ConversationPhrases.Pill(Waiting(Option<DateTimeOffset>.None)));
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
