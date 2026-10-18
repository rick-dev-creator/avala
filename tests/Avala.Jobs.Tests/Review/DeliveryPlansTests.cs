using Avala.Jobs.Contracts;
using Avala.Jobs.Tests.Coordination;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Jobs.Tests.Review;

public sealed class DeliveryPlansTests
{
    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Theory]
    [InlineData(null, "keep")]
    [InlineData("merge", "merge")]
    [InlineData("pull-request", "pull-request")]
    public async Task AJobIsPlannedThroughItsRepositorysStrategyFromItsBranchIntoItsBaseAsync(string? declared, string expected)
    {
        var flow = JobFlow.With();
        flow.Defaults.Approval = declared.ToOption();
        var job = await flow.RunningAsync();
        var worktree = Outcomes.Succeeds(await flow.Workspaces.FindAsync(Outcomes.Present(job.Workspace), Cancellation));

        var plan = Outcomes.Succeeds(await flow.Plans.PlanAsync(job.Id, Cancellation));

        Assert.Equal(new DeliveryPlan(expected, worktree.Branch, "main", worktree.Path), plan);
    }

    [Fact]
    public async Task AChildJobIsPlannedIntoItsParentAsync()
    {
        var flow = JobFlow.With();
        var parent = await flow.RunningAsync();
        var child = await flow.RunningAsync(JobFlow.Request() with { Instruction = "Write the notes", Parent = parent.Id });

        var plan = Outcomes.Succeeds(await flow.Plans.PlanAsync(child.Id, Cancellation));

        Assert.Equal(("merge", true), (plan.Strategy, plan.IntoParent));
    }

    [Fact]
    public async Task AnUnreadableJobFileOrAnUnknownJobHasNoPlanAsync()
    {
        var flow = JobFlow.With();
        flow.Defaults.Approval = Result<Option<string>, JobRejection>.Failure(JobRejection.InvalidJobFile);
        var job = await flow.RunningAsync();

        Assert.Equal(JobRejection.InvalidJobFile, Outcomes.FailsWith(await flow.Plans.PlanAsync(job.Id, Cancellation)));
        Assert.Equal(JobRejection.UnknownJob, Outcomes.FailsWith(await flow.Plans.PlanAsync(JobId.New(), Cancellation)));
    }
}
