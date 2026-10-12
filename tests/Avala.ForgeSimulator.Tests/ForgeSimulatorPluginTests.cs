using Avala.Forges.Contracts;
using Avala.Runtime;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.ForgeSimulator.Tests;

public sealed class ForgeSimulatorPluginTests
{
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task TheSimulatedForgeSignsInOnlyInDeveloperModeAsync(bool developer, int logins)
    {
        await using var data = new TemporaryFolder();
        await using var composition = PluginComposition.Of(new ForgeSimulatorPlugin(developer), new AvalaPaths(data.Path), services => services.AddRuntime(new AvalaPaths(data.Path)));

        Assert.Equal(logins, composition.Get<IEnumerable<IForgeLogins>>().Count());
    }
}
