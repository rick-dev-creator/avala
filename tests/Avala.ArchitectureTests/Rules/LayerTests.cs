using Avala.ArchitectureTests.Ddd;
using Avala.ArchitectureTests.Scopes;

namespace Avala.ArchitectureTests.Rules;

public sealed class LayerTests
{
    private static CodeScope Violating => CodeScopes.Of(Scope.Violating);

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void LayersRespectTheirDirection(Scope scope) =>
        Assert.Empty(LayerRules.CrossForbiddenLayers(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsDependenciesAgainstTheLayerDirection() =>
        Assert.Equal(
            ["ApplicationAwarePolicy", "DomainAwareViewModel", "InfrastructureAwareService", "InfrastructureAwareViewModel"],
            Violations.Named(LayerRules.CrossForbiddenLayers(Violating)));

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void ContractsHoldNoLogic(Scope scope) =>
        Assert.Empty(LayerRules.ContractsWithLogic(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsContractsWithLogic() =>
        Assert.Equal(["LedgerCalculator"], Violations.Named(LayerRules.ContractsWithLogic(Violating)));
}
