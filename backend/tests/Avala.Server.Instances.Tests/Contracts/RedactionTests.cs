using Avala.Server.Instances.Contracts.Authentication;
using Avala.Server.Instances.Contracts.Registration;
using Avala.Server.Instances.Domain;
using Avala.Server.Instances.EndPoints.Registration;

namespace Avala.Server.Instances.Tests.Contracts;

public sealed class RedactionTests
{
    [Fact]
    public void AnAuthenticationQueryNeverShowsItsKey()
    {
        var key = InstanceKey.Issue();

        Assert.DoesNotContain(key, new AuthenticateInstance(key).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ARegisteredInstanceNeverShowsItsKey()
    {
        var key = InstanceKey.Issue();

        Assert.DoesNotContain(key, new RegisteredInstance(Guid.NewGuid(), "desktop", DateTimeOffset.UnixEpoch, key).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ARegistrationResponseNeverShowsItsKey()
    {
        var key = InstanceKey.Issue();

        Assert.DoesNotContain(key, new RegisterInstanceResponse(Guid.NewGuid(), "desktop", DateTimeOffset.UnixEpoch, key).ToString(), StringComparison.Ordinal);
    }
}
