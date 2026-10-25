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

    public static TheoryData<string> Kinds => new() { "jobSubmitted", "jobModelChosen", "jobProgressed", "jobHeld", "usageReported", "attemptVerified" };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AnEmptyJobIdIsRejected(string kind)
    {
        var state = new IngestionState(FleetInstance.Register(Instance, "desktop", Noon));
        var application = new EventApplication(Instance, state);

        var refusal = application.Apply(Naming(kind, Guid.Empty), new EventTime(Noon, Clamped: false));

        Assert.Equal(("the job id is empty", 0m), (refusal, state.Instance.Tokens.Input));
    }

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
