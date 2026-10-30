using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Ingestion;
using Avala.Server.Activity.Domain;
using Avala.Server.Activity.Infrastructure.Ingestion;
using CostBasis = Avala.Server.Activity.Contracts.CostBasis;
using HoldReason = Avala.Server.Activity.Contracts.HoldReason;
using JobStatus = Avala.Server.Activity.Contracts.JobStatus;
using VerificationOutcome = Avala.Server.Activity.Contracts.VerificationOutcome;

namespace Avala.Server.Activity.Tests.Infrastructure;

public sealed class EventApplicationTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Instance = Guid.NewGuid();

    private readonly IngestionState state = new(FleetInstance.Register(Instance, "desktop", Noon));

    public static TheoryData<string> Kinds => new() { "jobSubmitted", "jobModelChosen", "jobProgressed", "jobHeld", "usageReported", "attemptVerified" };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AnEmptyJobIdIsRejected(string kind)
    {
        var refusal = Applied(Naming(kind, Guid.Empty));

        Assert.Equal(("the job id is empty", 0m), (refusal, state.Instance.Tokens.Input));
    }

    [Fact]
    public void ASubmissionKeepsTheJobsRepositoryParentAndModel()
    {
        var job = Guid.NewGuid();
        var parent = Guid.NewGuid();

        var refusal = Applied(new JobSubmittedEvent(Guid.NewGuid(), Noon, job, "remote:GitHub.com/Rick/Avala.git", parent, "opus", "high"));

        var submitted = state.Known(job)!;
        Assert.Equal(
            (null, "remote:github.com/rick/avala", parent, "opus", "high"),
            (refusal, submitted.Repository?.Value, submitted.ParentId, submitted.Model, submitted.Effort));
    }

    [Fact]
    public void UsageOfAJobIsAddedToTheJobAndToItsModelsQuarter()
    {
        var job = Guid.NewGuid();

        var refusal = Applied(new UsageReportedEvent(Guid.NewGuid(), Noon, job, 5, 0, 0, 0, 0, new IncomingCost(1m, "USD", CostBasis.Billed), "opus"), Noon.AddMinutes(7));

        var quarter = state.BucketFor(Instance, job, Noon, "opus");
        Assert.Equal(
            (null, 5m, 5m, 1m, 0m),
            (refusal, state.Known(job)!.Tokens.Input, quarter.Tokens.Input, Assert.Single(quarter.Costs).Amount, state.Instance.Tokens.Input));
    }

    [Fact]
    public void UsageOutsideAnyJobIsAddedToTheInstanceAndToItsQuarter()
    {
        var refusal = Applied(new UsageReportedEvent(Guid.NewGuid(), Noon, null, 5, 0, 0, 0, 0, null, null));

        Assert.Equal(
            (null, 5m, 5m),
            (refusal, state.Instance.Tokens.Input, state.BucketFor(Instance, QuarterUsage.OutsideAnyJob, Noon, string.Empty).Tokens.Input));
    }

    [Fact]
    public void RefusedUsageCountsNowhere()
    {
        var job = Guid.NewGuid();

        var refusal = Applied(new UsageReportedEvent(Guid.NewGuid(), Noon, job, 5, 0, 0, 0, 0, new IncomingCost(1m, "usd", CostBasis.Billed), null));

        Assert.Equal(("a currency is three uppercase letters", null, 0m), (refusal, state.Known(job), state.Instance.Tokens.Input));
    }

    private string? Applied(IIncomingEvent incoming, DateTimeOffset? at = null) =>
        new EventApplication(Instance, state).Apply(incoming, new EventTime(at ?? Noon, Clamped: false));

    private static IIncomingEvent Naming(string kind, Guid job) => kind switch
    {
        "jobSubmitted" => new JobSubmittedEvent(Guid.NewGuid(), Noon, job, "remote:github.com/rick/avala", null, null, null),
        "jobModelChosen" => new JobModelChosenEvent(Guid.NewGuid(), Noon, job, "opus", null),
        "jobProgressed" => new JobProgressedEvent(Guid.NewGuid(), Noon, job, JobStatus.Running),
        "jobHeld" => new JobHeldEvent(Guid.NewGuid(), Noon, job, HoldReason.Stalled),
        "usageReported" => new UsageReportedEvent(Guid.NewGuid(), Noon, job, 5, 0, 0, 0, 0, new IncomingCost(1m, "USD", CostBasis.Billed), null),
        "attemptVerified" => new AttemptVerifiedEvent(Guid.NewGuid(), Noon, job, 1, VerificationOutcome.Passed),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No such kind."),
    };
}
