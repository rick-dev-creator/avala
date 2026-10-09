using Avala.Agents.Contracts.Connections;
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
    [InlineData(JobRejection.InvalidRequest, "The job refused the command.")]
    public void ARefusedCommandToAJobSaysWhy(JobRejection rejection, string phrase) =>
        Assert.Equal(phrase, ConversationPhrases.Rejection(rejection));

    [Theory]
    [InlineData(JobStatus.Running, "The agent is working · interrupt it to step in")]
    [InlineData(JobStatus.NeedsHelp, "Continue the job with a message…")]
    [InlineData(JobStatus.AwaitingReview, "Send the agent more to do before you review…")]
    [InlineData(JobStatus.Checking, "The checks are running…")]
    [InlineData(JobStatus.Preparing, "The agent is starting…")]
    [InlineData(JobStatus.Draft, "The agent is starting…")]
    [InlineData(JobStatus.Approved, "This job has ended")]
    [InlineData(JobStatus.Discarded, "This job has ended")]
    [InlineData(JobStatus.Failed, "This job has ended")]
    public void TheComposerSaysWhatAMessageDoesForEachStatus(JobStatus status, string phrase) =>
        Assert.Equal(phrase, ConversationPhrases.Placeholder(status));

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
