using Avala.ArchitectureTests.Ddd;
using Avala.ArchitectureTests.Scopes;

namespace Avala.ArchitectureTests.Rules;

public sealed class CapabilityTests
{
    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void CapabilityComponentsAreSealedImmutableRecordsInContracts(Scope scope) =>
        Assert.Empty(CapabilityRules.MisshapenComponents(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsComponentsThatAreNotSealedImmutableRecordsInContracts() =>
        Assert.Equal(
            ["ClassCapability", "MisplacedCapability", "SettableCapability"],
            Violations.Named(CapabilityRules.MisshapenComponents(CodeScopes.Of(Scope.Violating))));
}
