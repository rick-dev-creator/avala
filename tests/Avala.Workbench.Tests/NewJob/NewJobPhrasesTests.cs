using Avala.Jobs.Contracts;
using Avala.Workbench.NewJob;

namespace Avala.Workbench.Tests.NewJob;

public sealed class NewJobPhrasesTests
{
    [Theory]
    [InlineData(JobRejection.EmptyRepository, "Name the repository the job works in.")]
    [InlineData(JobRejection.EmptyInstruction, "Write what the agent should do.")]
    [InlineData(JobRejection.UnusableConnection, "That connection cannot be used: its credential or the repository's jobs.json is not usable.")]
    [InlineData(JobRejection.WorkspaceUnavailable, "The repository's worktree could not be prepared.")]
    [InlineData(JobRejection.AgentUnavailable, "The agent could not start.")]
    [InlineData(JobRejection.InvalidRequest, "The request is invalid.")]
    [InlineData(JobRejection.InvalidAttemptBudget, "The number of attempts is invalid.")]
    [InlineData(JobRejection.UnknownParent, "The job was refused.")]
    public void ARefusedSubmissionSaysWhatToFix(JobRejection rejection, string phrase) =>
        Assert.Equal(phrase, NewJobPhrases.Rejection(rejection));
}
