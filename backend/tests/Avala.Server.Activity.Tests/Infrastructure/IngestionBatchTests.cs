using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Ingestion;
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
}
