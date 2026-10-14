using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Projects.Storage;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Projects.Tests.Storage;

public sealed class SqliteProjectStoreTests
{
    [Fact]
    public async Task ProjectsPlacementsAndTheArchiveSurviveAReopeningAsync()
    {
        await using var folder = new TemporaryFolder();
        var ledger = new ProjectRecord(ProjectId.New(), "ledger", Samples.Ledger);
        var website = new ProjectRecord(ProjectId.New(), "Website", Option<string>.None);
        var job = new JobId(Guid.NewGuid());
        var archived = new JobId(Guid.NewGuid());
        var restored = new JobId(Guid.NewGuid());

        await using (var first = new SqliteProjectStore(new AvalaPaths(folder.Path)))
        {
            await first.RunAsync(Samples.Cancellation);
            await first.KeepAsync(ledger, Samples.Cancellation);
            await first.KeepAsync(website, Samples.Cancellation);
            await first.PlaceAsync(job, ledger.Project, Samples.Cancellation);
            await first.PlaceAsync(job, website.Project, Samples.Cancellation);
            await first.ArchiveAsync([archived, restored], Samples.Cancellation);
            await first.ArchiveAsync([archived], Samples.Cancellation);
            await first.RestoreAsync(restored, Samples.Cancellation);
        }

        await using var second = new SqliteProjectStore(new AvalaPaths(folder.Path));
        await second.RunAsync(Samples.Cancellation);
        var map = await second.ReadAsync(Samples.Cancellation);

        Assert.Equal([ledger, website], map.Projects);
        Assert.Equal(website.Project, Assert.Single(map.Placements).Value);
        Assert.Equal([archived], map.Archived);
    }
}
