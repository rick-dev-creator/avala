using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Jobs.Contracts;

namespace Avala.Delegation.Tests.Records;

public sealed class ToolAnswersTests
{
    private static readonly ItemId Item = new("delegate");

    [Theory]
    [InlineData(JobFailure.WorkspaceUnavailable, "the child's worktree could not be prepared or checkpointed")]
    [InlineData(JobFailure.ConnectionUnavailable, "no connection could run the child")]
    [InlineData(JobFailure.ModelUnavailable, "the child's connection does not offer the model or effort it asked for")]
    [InlineData(JobFailure.AgentFailed, "the child's agent ended its turn in failure")]
    [InlineData(JobFailure.AgentUnavailable, "the child's agent could not take its message")]
    public void AFailedChildsReportToItsParentSaysWhyItFailed(JobFailure failure, string reason)
    {
        var report = new ChildReport(JobId.New(), ChildOutcome.Failed, JobStatus.Failed, DateTimeOffset.UnixEpoch) { Failure = failure };

        var answer = ToolAnswers.Reported(Item, new DelegationRecord(SessionId.New(), Item, "Build the calculator", DateTimeOffset.UnixEpoch), report);

        Assert.Equal(reason, (string?)JsonNode.Parse(answer.Content)?["reason"]);
    }
}
