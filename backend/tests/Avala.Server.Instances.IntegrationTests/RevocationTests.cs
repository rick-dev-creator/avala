using Ardalis.Result;
using Avala.Server.Instances.Contracts.Registration;
using Avala.Server.Instances.Contracts.Revocation;
using Avala.Server.Instances.Infrastructure.Registration;
using Avala.Server.Instances.Infrastructure.Revocation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Server.Instances.IntegrationTests;

public sealed class RevocationTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Noon = PostgresFixture.Noon;

    private readonly FakeTimeProvider clock = new(PostgresFixture.Noon);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RevokingMarksTheInstanceRevokedAsync()
    {
        await database.BeginAsync();
        await using var registering = database.Context();
        var registered = await new RegisterInstanceHandler(registering, clock).Handle(new RegisterInstance("desktop"), Cancellation);
        clock.Advance(TimeSpan.FromHours(1));

        await using var revoking = database.Context();
        var revoked = await new RevokeInstanceHandler(revoking, clock).Handle(new RevokeInstance(registered.Value.Id), Cancellation);

        await using var reading = database.Context();
        Assert.Equal((ResultStatus.NoContent, Noon.AddHours(1)), (revoked.Status, (await reading.Instances.SingleAsync(Cancellation)).RevokedAt));
    }

    [Fact]
    public async Task RevokingAnUnknownInstanceIsNotFoundAsync()
    {
        await database.BeginAsync();
        await using var context = database.Context();

        var revoked = await new RevokeInstanceHandler(context, clock).Handle(new RevokeInstance(Guid.NewGuid()), Cancellation);

        Assert.Equal(ResultStatus.NotFound, revoked.Status);
    }
}
