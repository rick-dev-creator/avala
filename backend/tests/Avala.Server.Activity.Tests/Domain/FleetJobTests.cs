using Avala.Server.Activity.Domain;

namespace Avala.Server.Activity.Tests.Domain;

public sealed class FleetJobTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly RepositoryKey Avala = RepositoryKey.Create("remote:github.com/rick/avala").Value;

    private static readonly ModelChoice NoModel = new(null, null);

    [Fact]
    public void AJobKeepsTheLatestStatusWhateverTheOrder()
    {
        var job = Started();

        job.Progress(JobStatus.Running, At(2));
        job.Progress(JobStatus.Preparing, At(1));

        Assert.Equal(JobStatus.Running, job.Status);
    }

    [Fact]
    public void ATieKeepsTheLaterApplied()
    {
        var job = Started();

        job.Progress(JobStatus.Running, At(1));
        job.Progress(JobStatus.Checking, At(1));

        Assert.Equal(JobStatus.Checking, job.Status);
    }

    [Fact]
    public void AStatusAfterAHoldClearsIt()
    {
        var job = Started();

        job.HoldFor(HoldReason.Stalled, At(1));
        job.Progress(JobStatus.Running, At(2));

        Assert.Null(job.VisibleHold);
    }

    [Fact]
    public void AHoldAfterTheStatusShows()
    {
        var job = Started();

        job.Progress(JobStatus.NeedsHelp, At(1));
        job.HoldFor(HoldReason.Stalled, At(2));

        Assert.Equal(HoldReason.Stalled, job.VisibleHold);
    }

    [Fact]
    public void AnEventBeforeItsSubmissionCreatesTheJobWithAnUnknownRepository()
    {
        var job = Started();

        job.Progress(JobStatus.Running, At(1));

        Assert.Null(job.Repository);
    }

    [Fact]
    public void ALateSubmissionFillsTheRepositoryAndKeepsTheNewerStatus()
    {
        var job = Started();

        job.Progress(JobStatus.Running, At(2));
        job.Submit(Avala, parentId: null, NoModel, At(1));

        Assert.Equal((Avala, JobStatus.Running), (job.Repository, job.Status));
    }

    [Fact]
    public void ASubmissionNamingAnotherRepositoryIsRefused()
    {
        var job = Started();

        job.Submit(Avala, parentId: null, NoModel, At(1));

        Assert.Equal(
            "the job already belongs to another repository",
            FleetJob.RefusesSubmission(job, RepositoryKey.Create("remote:github.com/rick/other").Value));
    }

    [Fact]
    public void ALaterModelChoiceReplacesTheModel()
    {
        var job = Started();

        job.Submit(Avala, parentId: null, new ModelChoice("opus", "high"), At(1));
        job.ChooseModel(new ModelChoice("sonnet", "low"), At(2));

        Assert.Equal(("sonnet", "low"), (job.Model, job.Effort));
    }

    [Fact]
    public void TheHighestAttemptsOutcomeIsKept()
    {
        var job = Started();

        job.Verify(new Attempt(2, VerificationOutcome.Failed), At(2));
        job.Verify(new Attempt(1, VerificationOutcome.Passed), At(3));

        Assert.Equal((2, VerificationOutcome.Failed), (job.Attempts, job.LastOutcome));
    }

    [Fact]
    public void TheLatestOfEqualAttemptsIsKept()
    {
        var job = Started();

        job.Verify(new Attempt(2, VerificationOutcome.Failed), At(2));
        job.Verify(new Attempt(2, VerificationOutcome.Passed), At(3));
        job.Verify(new Attempt(2, VerificationOutcome.Failed), At(1));

        Assert.Equal(VerificationOutcome.Passed, job.LastOutcome);
    }

    [Fact]
    public void SpendingAddsTokensAndCostPerCurrencyAndBasis()
    {
        var job = Started();

        job.Spend(new TokenCounts(10, 1, 0, 0, 0), new CostAmount(0.5m, "USD", CostBasis.Billed), At(1));
        job.Spend(new TokenCounts(5, 2, 0, 0, 0), new CostAmount(0.25m, "USD", CostBasis.Billed), At(2));
        job.Spend(new TokenCounts(0, 0, 0, 0, 0), new CostAmount(2m, "USD", CostBasis.ApiEquivalent), At(3));

        Assert.Equal(
            (15m, 3m, "Billed 0.75, ApiEquivalent 2"),
            (job.Tokens.Input, job.Tokens.Output, string.Join(", ", job.Costs.Select(cost => $"{cost.Basis} {cost.Amount}"))));
    }

    [Fact]
    public void FirstAndLastActivityAreTheEarliestAndLatestEventOfAnyKind()
    {
        var job = FleetJob.Start(Guid.NewGuid(), Guid.NewGuid(), At(2));

        job.Spend(new TokenCounts(1, 0, 0, 0, 0), cost: null, At(5));
        job.Progress(JobStatus.Running, At(1));

        Assert.Equal((Noon.AddMinutes(1), Noon.AddMinutes(5)), (job.FirstActivityAt, job.LastActivityAt));
    }

    [Fact]
    public void UsageOutsideAJobCountsTowardTheInstance()
    {
        var instance = FleetInstance.Register(Guid.NewGuid(), "desktop", Noon);

        instance.Spend(new TokenCounts(7, 0, 0, 0, 0), new CostAmount(1m, "EUR", CostBasis.Billed));

        Assert.Equal((7m, 1m), (instance.Tokens.Input, instance.Costs.Single().Amount));
    }

    private static FleetJob Started() => FleetJob.Start(Guid.NewGuid(), Guid.NewGuid(), At(0));

    private static EventTime At(int minutes) => new(Noon.AddMinutes(minutes), Clamped: false);
}
