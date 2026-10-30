using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Ingestion;
using QuarterUsage = Avala.Server.Activity.Domain.QuarterUsage;
using Avala.Server.Activity.Infrastructure.Ingestion;

namespace Avala.Server.Activity.Tests.Infrastructure;

public sealed class IngestionBatchTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ARepeatedIdIsAdmittedAndListedOnce()
    {
        var id = Guid.NewGuid();
        var job = Guid.NewGuid();
        var batch = new IngestionBatch(
            [new JobProgressedEvent(id, Noon, job, JobStatus.Running), new JobProgressedEvent(id, Noon, job, JobStatus.Failed)],
            Noon.AddHours(1));

        batch.Sort(new HashSet<Guid>());

        Assert.Equal(
            (id, JobStatus.Running),
            (Assert.Single(batch.Ids), Assert.IsType<JobProgressedEvent>(Assert.Single(batch.InOrder).Event).Status));
    }

    [Fact]
    public void TheJobsAndQuartersOfTheAdmittedUsageAreListedOnce()
    {
        var job = Guid.NewGuid();
        var batch = new IngestionBatch(
            [
                Usage(job, Noon.AddMinutes(1)),
                Usage(job, Noon.AddMinutes(14)),
                Usage(null, Noon.AddMinutes(16)),
                new JobProgressedEvent(Guid.NewGuid(), Noon.AddMinutes(40), Guid.NewGuid(), JobStatus.Running),
            ],
            Noon.AddHours(1));
        batch.Sort(new HashSet<Guid>());

        var (jobs, quarters) = batch.UsageKeys;

        Assert.Equal(
            ($"{job}, {QuarterUsage.OutsideAnyJob}", $"{Noon:O}, {Noon.AddMinutes(15):O}"),
            (string.Join(", ", jobs), string.Join(", ", quarters.Select(quarter => quarter.ToString("O", System.Globalization.CultureInfo.InvariantCulture)))));
    }

    private static UsageReportedEvent Usage(Guid? job, DateTimeOffset at) =>
        new(Guid.NewGuid(), at, job, 1, 0, 0, 0, 0, null, null);
}
