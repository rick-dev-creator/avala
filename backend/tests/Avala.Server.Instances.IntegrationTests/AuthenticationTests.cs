using Ardalis.Result;
using Avala.Server.Instances.Contracts.Authentication;
using Avala.Server.Instances.Contracts.Registration;
using Avala.Server.Instances.Contracts.Revocation;
using Avala.Server.Instances.Domain;
using Avala.Server.Instances.Infrastructure.Authentication;
using Avala.Server.Instances.Infrastructure.Registration;
using Avala.Server.Instances.Infrastructure.Revocation;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Server.Instances.IntegrationTests;

public sealed class AuthenticationTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private readonly FakeTimeProvider clock = new(PostgresFixture.Noon);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AStoredKeyAuthenticatesItsInstanceAsync()
    {
        await database.BeginAsync();
        var registered = await RegisterAsync();

        var authenticated = await AuthenticateAsync(registered.Key);

        Assert.Equal(new AuthenticatedInstance(registered.Id, "desktop"), authenticated.Value);
    }

    [Fact]
    public async Task AnUnknownKeyIsUnauthorizedAsync()
    {
        await database.BeginAsync();
        await RegisterAsync();

        var authenticated = await AuthenticateAsync(InstanceKey.Issue());

        Assert.Equal(ResultStatus.Unauthorized, authenticated.Status);
    }

    [Fact]
    public async Task ARevokedKeyIsUnauthorizedAsync()
    {
        await database.BeginAsync();
        var registered = await RegisterAsync();
        await using (var revoking = database.Context())
        {
            await new RevokeInstanceHandler(revoking, clock).Handle(new RevokeInstance(registered.Id), Cancellation);
        }

        var authenticated = await AuthenticateAsync(registered.Key);

        Assert.Equal(ResultStatus.Unauthorized, authenticated.Status);
    }

    private async Task<RegisteredInstance> RegisterAsync()
    {
        await using var context = database.Context();

        return (await new RegisterInstanceHandler(context, clock).Handle(new RegisterInstance("desktop"), Cancellation)).Value;
    }

    private async Task<Result<AuthenticatedInstance>> AuthenticateAsync(string key)
    {
        await using var context = database.Context();

        return await new AuthenticateInstanceHandler(context).Handle(new AuthenticateInstance(key), Cancellation);
    }
}
