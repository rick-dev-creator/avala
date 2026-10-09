using Avala.Runtime.Updates;

namespace Avala.Runtime.Tests.Updates;

public sealed class ReleaseVersionTests
{
    [Fact]
    public void VersionsArePrecededAsSemanticVersioningsOwnExampleOrdersThem()
    {
        string[] semverOrg = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.9.0", "1.10.0", "1.11.0", "2.0.0", "2.1.0", "2.1.1"];
        var parsed = semverOrg.Select(text => ReleaseVersion.Parse(text).Match(version => version, () => throw new InvalidOperationException(text))).ToList();

        var ordered = parsed.Order(Comparer<ReleaseVersion>.Create(ReleaseVersion.Compare)).Select(version => version.ToString());

        Assert.Equal(semverOrg, ordered);
        Assert.All(parsed.Zip(parsed.Skip(1)), pair => Assert.True(pair.Second.IsNewerThan(pair.First), $"{pair.Second} after {pair.First}"));
    }

    [Theory]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData("0.1.0-beta.1+5c371e6a1b2d", "0.1.0-beta.1")]
    [InlineData("V1.2.3", "1.2.3")]
    public void TagsAndInformationalVersionsAreRead(string text, string version) =>
        Assert.Equal(version, ReleaseVersion.Parse(text).Match(parsed => parsed.ToString(), () => "none"));

    [Theory]
    [InlineData("unknown")]
    [InlineData("1.2")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-beta..1")]
    [InlineData("1.-2.3")]
    public void AnythingElseIsNotAVersion(string text) =>
        Assert.True(ReleaseVersion.Parse(text).IsNone);
}
