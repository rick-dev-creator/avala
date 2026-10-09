namespace Avala.Sdk.Tests;

public sealed class AvalaBuildTests
{
    [Theory]
    [InlineData("0.9.0-beta.1+5c371e6a1b2d3e4f", "0.9.0-beta.1", "5c371e6a1b2d3e4f")]
    [InlineData("1.0.0", "1.0.0", "")]
    [InlineData("1.0.0+", "1.0.0", "")]
    [InlineData("", "unknown", "")]
    public void TheInformationalVersionGivesTheVersionAndTheCommit(string informational, string version, string commit) =>
        Assert.Equal(
            (version, commit),
            (AvalaBuild.From(informational).Version, AvalaBuild.From(informational).Commit.Match(found => found, () => string.Empty)));

    [Fact]
    public void WithoutAnInformationalVersionTheBuildIsUnknown() =>
        Assert.Equal(new AvalaBuild("unknown", Option<string>.None), AvalaBuild.From(Option<string>.None));
}
