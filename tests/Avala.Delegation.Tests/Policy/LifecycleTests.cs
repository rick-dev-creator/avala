using Avala.Agents.Contracts.Events;
using Avala.Delegation.Contracts;
using Avala.Delegation.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Tests.Policy;

public sealed class LifecycleTests
{
    [Theory]
    [InlineData(JobStatus.Running, "", "Nothing", "Working", "working")]
    [InlineData(JobStatus.Running, "", "Permission", "Waiting", "awaitingPermission")]
    [InlineData(JobStatus.NeedsHelp, "", "Answer", "Waiting", "awaitingAnswer")]
    [InlineData(JobStatus.NeedsHelp, "LimitNearlyReached", "Nothing", "Waiting", "limitNearlyReached")]
    [InlineData(JobStatus.NeedsHelp, "SessionLost", "Nothing", "Lost", "lost")]
    [InlineData(JobStatus.AwaitingReview, "", "Permission", "Completed", "completed")]
    [InlineData(JobStatus.Approved, "", "Nothing", "Completed", "completed")]
    [InlineData(JobStatus.Failed, "", "Nothing", "Failed", "failed")]
    [InlineData(JobStatus.Discarded, "", "Nothing", "Stopped", "stopped")]
    public void AJobsStatusHoldAndRequestGiveItsSubagentStateAndWhatItWaitsFor(JobStatus status, string hold, string asking, string state, string detail)
    {
        var held = hold.Length == 0 ? Option<HoldReason>.None : Enum.Parse<HoldReason>(hold);
        var asked = Enum.Parse<Asking>(asking);

        var found = Lifecycle.Of(status, held, asked);

        Assert.Equal((Enum.Parse<SubagentState>(state), detail), (found, Lifecycle.Status(found, asked, held)));
    }

    [Theory]
    [InlineData("", "Nothing", "Working")]
    [InlineData("", "Permission", "Waiting")]
    [InlineData("Succeeded", "Nothing", "Completed")]
    [InlineData("Failed", "Nothing", "Failed")]
    [InlineData("Expired", "Nothing", "Failed")]
    [InlineData("Cancelled", "Nothing", "Stopped")]
    [InlineData("Abandoned", "Nothing", "Lost")]
    public void AnItemsOutcomeGivesTheStateOfTheInProcessSubagentItRan(string outcome, string asking, string state) =>
        Assert.Equal(
            Enum.Parse<SubagentState>(state),
            Lifecycle.Of(outcome.Length == 0 ? Option<ItemOutcome>.None : Enum.Parse<ItemOutcome>(outcome), Enum.Parse<Asking>(asking)));
}
