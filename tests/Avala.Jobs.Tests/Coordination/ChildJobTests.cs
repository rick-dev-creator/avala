using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using JobAnnouncement = Avala.Jobs.Contracts.JobSubmitted;

namespace Avala.Jobs.Tests.Coordination;

public sealed class ChildJobTests
{
    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task AChildOfARunningJobIsStoredWithItsParentAndAnnouncedWithItAsync()
    {
        var flow = JobFlow.With();
        var parent = await flow.RunningAsync();

        var child = await flow.SubmittedAsync(ChildOf(parent));

        Assert.Equal((JobState.Preparing, Option<JobId>.Some(parent.Id)), (child.State, child.Parent));
        Assert.Contains(new JobAnnouncement(child.Id) { Parent = parent.Id }, flow.Bus.Published);
    }

    [Theory]
    [InlineData("UnknownParent", "unknown", "/repos/shop")]
    [InlineData("ParentNotRunning", "held", "/repos/shop")]
    [InlineData("InvalidRequest", "running", "/repos/other")]
    public async Task AChildIsRefusedWhenItsParentIsUnknownNotRunningOrInAnotherRepositoryAsync(string expected, string parentIs, string repository)
    {
        var flow = JobFlow.With();
        var parent = parentIs == "held" ? await flow.HeldAsync(HoldReason.Stalled) : await flow.RunningAsync();
        var named = parentIs == "unknown" ? JobId.New() : parent.Id;
        var stored = flow.Store.Jobs.Count;
        var published = flow.Bus.Published.Count;

        var rejected = await flow.Submit.ExecuteAsync(JobFlow.Request() with { RepositoryPath = repository, Parent = named }, Cancellation);

        Assert.Equal(Enum.Parse<JobRejection>(expected), Outcomes.FailsWith(rejected));
        Assert.Equal((stored, published), (flow.Store.Jobs.Count, flow.Bus.Published.Count));
    }

    [Fact]
    public async Task AChildStartsWithoutAdmissionFromACheckpointOfItsParentWithItsParentsRulesAsync()
    {
        var flow = JobFlow.With();
        var admitted = new List<JobId>();
        flow.Admissions.Add(new CountingAdmission(admitted));
        var parent = await flow.RunningAsync();
        var origin = Outcomes.Succeeds(await flow.Workspaces.FindAsync(Outcomes.Present(parent.Workspace), Cancellation));

        var child = await flow.RunningAsync(ChildOf(parent));

        Assert.Equal([parent.Id], admitted);
        Assert.Equal(JobState.Running, child.State);
        Assert.Equal([$"Delegated to job {child.Id.Value}"], flow.Workspaces.CheckpointsOf(origin.Id));
        Assert.Equal(new WorkspaceRequest("/repos/shop", origin.Branch) { Rules = origin.RulesCommit }, flow.Workspaces.Prepared[^1]);
    }

    [Fact]
    public async Task ApprovingAChildCheckpointsItsParentAndMergesTheWorkIntoItWhateverTheRepositoryDeclaresAsync()
    {
        var flow = JobFlow.With();
        var merge = new ScriptedStrategy("merge", new ApprovalDelivery("merge", "avala/parent", "abc123"));
        flow.Strategies.Add(merge);
        flow.Defaults.Approval = Option<string>.Some("keep");
        var parent = await flow.RunningAsync();
        var child = await ReviewedChildAsync(flow, parent);

        var approval = Outcomes.Succeeds(await flow.Jobs.ApproveAsync(child.Id, Cancellation));

        Assert.Equal(new ApprovalDelivery("merge", "avala/parent", "abc123"), approval.Delivery);
        Assert.Equal(child.Workspace, Option<WorkspaceId>.Some(Assert.Single(merge.Requests).Workspace.Id));
        Assert.Equal(
            [$"Delegated to job {child.Id.Value}", $"Before integrating job {child.Id.Value}"],
            flow.Workspaces.CheckpointsOf(Outcomes.Present(parent.Workspace)));
        Assert.Equal(JobState.Approved, child.State);
    }

    [Fact]
    public async Task AChildWhoseParentNoLongerRunsIsNotIntegratedAndStaysAwaitingReviewAsync()
    {
        var flow = JobFlow.With();
        var merge = new ScriptedStrategy("merge", new ApprovalDelivery("merge", "avala/parent", "abc123"));
        flow.Strategies.Add(merge);
        var parent = await flow.RunningAsync();
        var child = await ReviewedChildAsync(flow, parent);
        await flow.FinishTurnAsync(parent);

        Assert.Equal(JobRejection.ParentNotRunning, Outcomes.FailsWith(await flow.Jobs.ApproveAsync(child.Id, Cancellation)));

        Assert.Empty(merge.Requests);
        Assert.Equal(JobState.AwaitingReview, child.State);
    }

    private static JobRequest ChildOf(Job parent) => JobFlow.Request() with { Instruction = "Write the notes", Parent = parent.Id };

    private static async Task<Job> ReviewedChildAsync(JobFlow flow, Job parent)
    {
        var child = await flow.RunningAsync(ChildOf(parent));
        await flow.FinishTurnAsync(child);
        Assert.Equal(JobState.AwaitingReview, child.State);

        return child;
    }

    private sealed class CountingAdmission(List<JobId> admitted) : IJobAdmission
    {
        public ValueTask AdmitAsync(JobId job, CancellationToken cancellationToken)
        {
            admitted.Add(job);

            return ValueTask.CompletedTask;
        }
    }
}
