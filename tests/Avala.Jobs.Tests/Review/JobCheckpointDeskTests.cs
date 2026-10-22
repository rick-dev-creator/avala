using Avala.Jobs.Contracts;
using Avala.Jobs.Review;
using Avala.Jobs.Tests.Coordination;
using Avala.Testing;

namespace Avala.Jobs.Tests.Review;

public sealed class JobCheckpointDeskTests
{
    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task RestoringAnAttemptOfAJobAwaitingReviewRestoresTheCheckpointThatAttemptLeftAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        await flow.FinishTurnAsync(job);

        var restored = Outcomes.Succeeds(await Desk(flow).RestoreAsync(job.Id, 1, Cancellation));

        Assert.Equal(new CheckpointRestored(job.Id, 1, []), restored);
        Assert.Equal([1], flow.Workspaces.Restored);
    }

    [Fact]
    public async Task RestoringAnAttemptThatLeftNoCheckpointIsRefusedAsUnknownAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        await flow.FinishTurnAsync(job);

        Assert.Equal(JobRejection.UnknownCheckpoint, Outcomes.FailsWith(await Desk(flow).RestoreAsync(job.Id, 2, Cancellation)));
        Assert.Empty(flow.Workspaces.Restored);
    }

    [Fact]
    public async Task RestoringACheckpointOfARunningJobOrAnUnknownOneIsRefusedAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();

        Assert.Equal(JobRejection.CannotRestore, Outcomes.FailsWith(await Desk(flow).RestoreAsync(job.Id, 1, Cancellation)));
        Assert.Equal(JobRejection.UnknownJob, Outcomes.FailsWith(await Desk(flow).RestoreAsync(JobId.New(), 1, Cancellation)));
        Assert.Empty(flow.Workspaces.Restored);
    }

    private static JobCheckpointDesk Desk(JobFlow flow) => new(flow.Queues, flow.Workspaces);
}
