using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Jobs.Tests.Coordination;
using Avala.Testing;

namespace Avala.Jobs.Tests.Ledger;

public sealed class EndedJobsTests
{
    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task AJobAwaitingReviewWhoseAgentEndedIsRestingAsync()
    {
        var flow = JobFlow.With();
        var job = await ReviewedAsync(flow);
        _ = await flow.Agents.StopAsync(Outcomes.Present(job.Session), Cancellation);
        var worked = 0;

        var resting = await new EndedJobs(flow.Queues, flow.Agents).WhileRestingAsync(job.Id, _ => Task.FromResult(++worked), Cancellation);

        Assert.Equal((true, 1), (resting, worked));
    }

    [Fact]
    public async Task AJobWhoseAgentIsStillOpenOrThatIsRunningIsNotRestingAsync()
    {
        var flow = JobFlow.With();
        var reviewed = await ReviewedAsync(flow);
        var running = await flow.RunningAsync();
        var guard = new EndedJobs(flow.Queues, flow.Agents);
        var worked = 0;

        var open = await guard.WhileRestingAsync(reviewed.Id, _ => Task.FromResult(++worked), Cancellation);
        var busy = await guard.WhileRestingAsync(running.Id, _ => Task.FromResult(++worked), Cancellation);

        Assert.Equal((false, false, 0), (open, busy, worked));
    }

    private static async Task<Job> ReviewedAsync(JobFlow flow)
    {
        var job = await flow.RunningAsync();
        await flow.FinishTurnAsync(job);
        Assert.Equal(JobState.AwaitingReview, job.State);

        return job;
    }
}
