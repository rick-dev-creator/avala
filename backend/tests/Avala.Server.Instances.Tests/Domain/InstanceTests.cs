using Avala.Server.Instances.Domain;

namespace Avala.Server.Instances.Tests.Domain;

public sealed class InstanceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RegisteringTrimsTheName() =>
        Assert.Equal("desktop", Instance.Register("  desktop ", Noon).Instance.Name);

    [Fact]
    public void RegisteringKeepsOnlyTheHashOfTheKey()
    {
        var issued = Instance.Register("desktop", Noon);

        Assert.Equal(InstanceKey.Hash(issued.Key), issued.Instance.KeyHash);
    }

    [Fact]
    public void RevokingKeepsTheFirstRevocationTime()
    {
        var instance = Instance.Register("desktop", Noon).Instance;

        instance.Revoke(Noon.AddHours(1));
        instance.Revoke(Noon.AddHours(2));

        Assert.Equal(Noon.AddHours(1), instance.RevokedAt);
    }

    [Fact]
    public void AnIssuedInstanceNeverShowsItsKey()
    {
        var issued = Instance.Register("desktop", Noon);

        Assert.DoesNotContain(issued.Key, issued.ToString(), StringComparison.Ordinal);
    }
}
