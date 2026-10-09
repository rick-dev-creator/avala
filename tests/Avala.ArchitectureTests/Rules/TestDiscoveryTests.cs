using Avala.ArchitectureTests.Discovery;

namespace Avala.ArchitectureTests.Rules;

public sealed class TestDiscoveryTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TestAttributesNameOnlyTypesTheirTestAssemblyShipsAsync()
    {
        var host = Assert.Single(TestAssembly.All, assembly => assembly.Name == "Avala.Host.Tests");
        Assert.False(host.Ships("Avala.Jobs.Contracts"), "The host tests load the module contracts from the plugin folder.");

        var violations = (await Task.WhenAll(TestAssembly.All.Select(assembly => assembly.UnshippedAttributeTypesAsync(Cancellation))))
            .SelectMany(found => found)
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}
