using Ardalis.Result;
using Avala.Server.Instances.Contracts.Lookup;
using Avala.Server.Instances.Contracts.Registration;
using Avala.Server.Instances.Infrastructure.Lookup;
using Avala.Server.Instances.Infrastructure.Registration;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Server.Instances.IntegrationTests;

public sealed class LookupTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Noon = PostgresFixture.Noon;

    private readonly FakeTimeProvider clock = new(PostgresFixture.Noon);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FindingAnswersTheStoredInstanceAsync()
    {
        await database.BeginAsync();
        await using var context = database.Context();
        var registered = await new RegisterInstanceHandler(context, clock).Handle(new RegisterInstance("desktop"), Cancellation);

        await using var reading = database.Context();
        var found = await new FindInstanceHandler(reading).Handle(new FindInstance(registered.Value.Id), Cancellation);

        Assert.Equal(new InstanceDetails(registered.Value.Id, "desktop", Noon, null), found.Value);
    }

    [Fact]
    public async Task FindingAnUnknownInstanceIsNotFoundAsync()
    {
        await database.BeginAsync();
        await using var context = database.Context();

        var found = await new FindInstanceHandler(context).Handle(new FindInstance(Guid.NewGuid()), Cancellation);

        Assert.Equal(ResultStatus.NotFound, found.Status);
    }
}
