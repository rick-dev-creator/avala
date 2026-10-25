using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Discovering;

namespace Avala.Verification.Tests.Discovering;

public sealed class DiscoverySettingsTests
{
    [Theory]
    [InlineData("""{ "discoverChecks": false }""", false)]
    [InlineData("""{ "discoverChecks": true }""", true)]
    [InlineData("""{ "discoverChecks": "no" }""", true)]
    [InlineData("""{}""", true)]
    [InlineData("""[]""", true)]
    [InlineData("""not json""", true)]
    public void OnlyAnExplicitFalseTurnsDiscoveryOff(string settings, bool discovers) =>
        Assert.Equal(discovers, DiscoverySettings.Discovers(settings));

    [Fact]
    public async Task DiscoveryIsOnWithoutTheFileAndFollowsItOnceWrittenAsync()
    {
        await using var data = new TemporaryFolder();
        var settings = new DiscoverySettings(new AvalaPaths(data.Path));
        var before = await settings.DiscoversAsync(TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(Path.Combine(data.Path, DiscoverySettings.FileName), """{ "discoverChecks": false }""", TestContext.Current.CancellationToken);

        Assert.Equal((true, false), (before, await settings.DiscoversAsync(TestContext.Current.CancellationToken)));
    }
}
