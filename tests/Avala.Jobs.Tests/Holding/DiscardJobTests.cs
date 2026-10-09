using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Tests.Coordination;
using Avala.Testing;

namespace Avala.Jobs.Tests.Holding;

public sealed class DiscardJobTests
{
    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task DiscardingARunningJobStopsItsSessionBeforeAnnouncingItAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        var session = Outcomes.Present(job.Session);

        Assert.Equal(job.Id, Outcomes.Succeeds(await flow.Jobs.DiscardAsync(job.Id, Cancellation)));

        Assert.Equal(JobState.Discarded, job.State);
        Assert.Equal([session], flow.Agents.Stopped);
        Assert.Equal(new JobProgressed(job.Id, JobStatus.Discarded), flow.Bus.Published[^1]);
    }

    [Fact]
    public async Task OnlyAnOpenJobCanBeDiscardedAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        await flow.FinishTurnAsync(job, TurnOutcome.Failed);
        var published = flow.Bus.Published.Count;

        Assert.Equal(JobRejection.NotDiscardable, Outcomes.FailsWith(await flow.Jobs.DiscardAsync(job.Id, Cancellation)));
        Assert.Equal(JobRejection.UnknownJob, Outcomes.FailsWith(await flow.Jobs.DiscardAsync(JobId.New(), Cancellation)));
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(published, flow.Bus.Published.Count);
    }
}
