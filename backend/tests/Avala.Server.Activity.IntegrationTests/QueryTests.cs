using Avala.Server.Activity.Contracts;
using static Avala.Server.Activity.IntegrationTests.Fleet;

namespace Avala.Server.Activity.IntegrationTests;

public sealed class QueryTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Desktop = Guid.Parse("0199f6a0-0000-7000-8000-00000000000a");

    private static readonly Guid Laptop = Guid.Parse("0199f6a0-0000-7000-8000-00000000000b");

    private readonly Fleet fleet = new(database);

    [Fact]
    public async Task RepositoriesSumJobsInstancesTokensAndCostInSqlAsync()
    {
        await database.BeginAsync();
        Guid[] jobs = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        await fleet.IngestAsync(Desktop, "desktop", Submitted(Guid.NewGuid(), jobs[0], AvalaRemote, Noon), Usage(Guid.NewGuid(), jobs[0], 100, 1m));
        await fleet.IngestAsync(Desktop, "desktop", Submitted(Guid.NewGuid(), jobs[1], AvalaRemote, Noon), Usage(Guid.NewGuid(), jobs[1], 50, 0.5m));
        await fleet.IngestAsync(Laptop, "laptop", Submitted(Guid.NewGuid(), jobs[2], AvalaRemote, Noon), Usage(Guid.NewGuid(), jobs[2], 25, 0.25m));

        var repository = Assert.Single((await fleet.RepositoriesAsync()).Repositories);

        Assert.Equal(
            (AvalaRemote, 3, 2, 175m, new CostSum("USD", CostBasis.ApiEquivalent, 1.75m)),
            (repository.Key, repository.Jobs, repository.Instances, repository.Tokens.Input, Assert.Single(repository.Costs)));
    }

    [Fact]
    public async Task FirstActivityIsTheEarliestEventOfTheRepositorysJobsAsync()
    {
        await database.BeginAsync();
        Guid[] jobs = [Guid.NewGuid(), Guid.NewGuid()];
        await fleet.IngestAsync(Desktop, "desktop", Submitted(Guid.NewGuid(), jobs[0], AvalaRemote, Noon.AddMinutes(10)));
        await fleet.IngestAsync(Desktop, "desktop", Progressed(Guid.NewGuid(), jobs[1], JobStatus.Running, Noon.AddMinutes(-30)), Submitted(Guid.NewGuid(), jobs[1], AvalaRemote, Noon));

        var repository = Assert.Single((await fleet.RepositoriesAsync()).Repositories);

        Assert.Equal((Noon.AddMinutes(-30), Noon.AddMinutes(10)), (repository.FirstActivityAt, repository.LastActivityAt));
    }

    [Fact]
    public async Task ARepositorysJobsShowEachInstanceByItsNameAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(Desktop, "desktop", Submitted(Guid.NewGuid(), Guid.NewGuid(), AvalaRemote, Noon));
        await fleet.IngestAsync(Laptop, "laptop", Submitted(Guid.NewGuid(), Guid.NewGuid(), AvalaRemote, Noon));

        var jobs = await fleet.JobsAsync(AvalaRemote);

        Assert.Equal(["desktop", "laptop"], jobs.Select(job => job.InstanceName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AnotherFormOfAKeyFindsTheSameRepositorysJobsAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(Desktop, "desktop", Submitted(Guid.NewGuid(), Guid.NewGuid(), "remote:github.com/rick/avala", Noon));

        Assert.Single(await fleet.JobsAsync("remote:GitHub.com/Rick/Avala.git"));
    }

    [Fact]
    public async Task JobsWithAnUnknownRepositoryAreCountedApartAsync()
    {
        await database.BeginAsync();
        await fleet.IngestAsync(Desktop, "desktop", Submitted(Guid.NewGuid(), Guid.NewGuid(), AvalaRemote, Noon), Progressed(Guid.NewGuid(), Guid.NewGuid(), JobStatus.Running, Noon));

        var repositories = await fleet.RepositoriesAsync();

        Assert.Equal((1, 1), (repositories.Repositories.Count, repositories.UnattributedJobs));
    }
}
