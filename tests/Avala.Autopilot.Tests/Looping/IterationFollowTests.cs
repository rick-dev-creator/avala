using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Autopilot.Tests.Looping;

public sealed class IterationFollowTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnIterationLeftToAPersonFollowsItsJobAndItsSpendWhileTheLoopRunsOnAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("checks", "Loosen the checks");
        pilot.Backlog.Add("greet", "Greet the team");
        var loop = await pilot.StartAsync();
        var left = await pilot.CleanAsync(1, ".avala/checks.json");
        var job = Outcomes.Present(left.Job);
        _ = await pilot.TakenAsync(2);

        pilot.Usage.Spent(job, Pilot.Usd(0.40m));
        await pilot.ProgressAsync(job, JobStatus.Approved);
        var followed = await pilot.Bus.WaitForAsync<LoopIterationFollowed>(_ => true, Cancellation);

        Assert.Equal((loop, 1, IterationOutcome.ApprovedByPerson), (followed.Loop, followed.Iteration.Number, followed.Iteration.Outcome));
        Assert.Equal([Pilot.Usd(0.40m)], followed.Iteration.Cost);
        Assert.Equal([ExceptionReason.RuleFileEdited], followed.Iteration.Exceptions);
        Assert.Equal(IterationOutcome.ApprovedByPerson, Outcomes.Present(pilot.Registry.DigestOf(loop)).Iterations[0].Outcome);
    }

    [Fact]
    public async Task AnIterationLeftToAPersonWhoseJobLaterFailsIsKeptAsAJobFailureAfterTheLoopEndedAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("checks", "Loosen the checks");
        var loop = await pilot.StartAsync();
        var job = Outcomes.Present((await pilot.CleanAsync(1, ".avala/checks.json")).Job);
        _ = await pilot.EndedAsync();

        await pilot.ProgressAsync(job, JobStatus.Failed);

        var followed = Assert.Single(pilot.Bus.Published.OfType<LoopIterationFollowed>()).Iteration;
        Assert.Equal((IterationOutcome.Failed, Option<FailureSignature>.Some(new FailureSignature(FailureSource.Job, "Failed"))), (followed.Outcome, followed.Failure));
        var kept = Assert.Single(Assert.Single(await pilot.Loops.LoopsAsync(Cancellation), record => record.Id == loop).Iterations);
        Assert.Equal(IterationOutcome.Failed, kept.Outcome);
    }

    [Fact]
    public async Task AProgressThatChangesNeitherTheOutcomeNorTheSpendOfAnIterationIsNotFollowedAsync()
    {
        await using var pilot = new Pilot();
        pilot.Backlog.Add("checks", "Loosen the checks");
        _ = await pilot.StartAsync();
        var job = Outcomes.Present((await pilot.CleanAsync(1, ".avala/checks.json")).Job);
        _ = await pilot.EndedAsync();

        await pilot.ProgressAsync(job, JobStatus.AwaitingReview);

        Assert.Empty(pilot.Bus.Published.OfType<LoopIterationFollowed>());
    }
}
