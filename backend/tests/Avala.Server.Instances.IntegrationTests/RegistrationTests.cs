using Ardalis.Result;
using Avala.Server.Instances.Contracts.Registration;
using Avala.Server.Instances.Domain;
using Avala.Server.Instances.Infrastructure.Registration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Server.Instances.IntegrationTests;

public sealed class RegistrationTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Noon = PostgresFixture.Noon;

    private readonly FakeTimeProvider clock = new(PostgresFixture.Noon);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RegisteringStoresTheInstanceAndAnswersCreatedWithItsKeyAsync()
    {
        await database.BeginAsync();
        Result<RegisteredInstance> registered;

        await using (var context = database.Context())
        {
            registered = await new RegisterInstanceHandler(context, clock).Handle(new RegisterInstance("desktop"), Cancellation);
        }

        await using var reading = database.Context();
        var stored = await reading.Instances.SingleAsync(Cancellation);

        Assert.Equal(
            (ResultStatus.Created, InstanceKey.Hash(registered.Value.Key), "desktop", Noon),
            (registered.Status, stored.KeyHash, stored.Name, stored.RegisteredAt));
    }
}
