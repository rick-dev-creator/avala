using Avala.Server.Instances.Domain;

namespace Avala.Server.Instances.Tests.Domain;

public sealed class InstanceKeyTests
{
    [Fact]
    public void AnIssuedKeyIsThePrefixAndFortyThreeBase64UrlCharacters() =>
        Assert.Matches("^avk_[A-Za-z0-9_-]{43}$", InstanceKey.Issue());

    [Fact]
    public void TwoIssuedKeysDiffer() =>
        Assert.NotEqual(InstanceKey.Issue(), InstanceKey.Issue());

    [Fact]
    public void AKeyHashesToSixtyFourLowercaseHexCharacters() =>
        Assert.Matches("^[0-9a-f]{64}$", InstanceKey.Hash(InstanceKey.Issue()));

    [Fact]
    public void TheSameKeyAlwaysHashesTheSame()
    {
        var key = InstanceKey.Issue();

        Assert.Equal(InstanceKey.Hash(key), InstanceKey.Hash(key));
    }
}
