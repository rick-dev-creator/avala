using Avala.Jobs.Contracts;
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
}
