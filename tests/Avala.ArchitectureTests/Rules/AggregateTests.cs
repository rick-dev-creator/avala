using Avala.ArchitectureTests.Ddd;
using Avala.ArchitectureTests.Scopes;

namespace Avala.ArchitectureTests.Rules;

public sealed class AggregateTests
{
    private static CodeScope Violating => CodeScopes.Of(Scope.Violating);

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void AggregatesExposeNoSetters(Scope scope) =>
        Assert.Empty(AggregateRules.ExposeSetters(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsExposedSetters() =>
        Assert.Equal(["PublicSetterLedger"], Violations.Named(AggregateRules.ExposeSetters(Violating)));

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void AggregatesExposeNoMutableCollections(Scope scope) =>
        Assert.Empty(AggregateRules.ExposeMutableCollections(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsExposedMutableCollections() =>
        Assert.Equal(["MutableCollectionLedger"], Violations.Named(AggregateRules.ExposeMutableCollections(Violating)));

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void AggregatesHaveOnlyPrivateConstructors(Scope scope) =>
        Assert.Empty(AggregateRules.ExposeConstructors(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsExposedConstructors() =>
        Assert.Equal(["PublicConstructorLedger"], Violations.Named(AggregateRules.ExposeConstructors(Violating)));

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void AggregateOperationsReturnResults(Scope scope) =>
        Assert.Empty(AggregateRules.HaveOperationsWithoutResult(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsOperationsWithoutResult() =>
        Assert.Equal(["VoidOperationLedger"], Violations.Named(AggregateRules.HaveOperationsWithoutResult(Violating)));

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void AggregatesReferenceOthersByIdOnly(Scope scope) =>
        Assert.Empty(AggregateRules.ReferenceOtherAggregates(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsReferencesToOtherAggregates() =>
        Assert.Equal(["ObjectReferenceLedger"], Violations.Named(AggregateRules.ReferenceOtherAggregates(Violating)));

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void AggregatesHaveStronglyTypedIdentifiers(Scope scope) =>
        Assert.Empty(AggregateRules.LackStrongIdentifiers(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsLooseIdentifiers() =>
        Assert.Equal(["LooseIdLedger"], Violations.Named(AggregateRules.LackStrongIdentifiers(Violating)));
}
