using Avala.ArchitectureTests.Ddd;
using Avala.ArchitectureTests.Scopes;

namespace Avala.ArchitectureTests.Rules;

public sealed class DomainPurityTests
{
    private static CodeScope Violating => CodeScopes.Of(Scope.Violating);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void DomainDependsOnlyOnAllowedTypes(Scope scope) =>
        Assert.Empty(DomainPurityRules.DependOnDisallowedTypes(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsDomainDependenciesOnIoTasksAndOuterLayers() =>
        Assert.Equal(
            ["ApplicationAwarePolicy", "AsyncPolicy", "DiskPolicy", "ForeignErrorLedger"],
            Violations.Named(DomainPurityRules.DependOnDisallowedTypes(Violating)));

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public async Task DomainNeverThrowsAsync(Scope scope) =>
        Assert.Empty(await DomainPurityRules.ThrowAsync(CodeScopes.Of(scope), Cancellation));

    [Fact]
    public async Task DetectsThrowingDomainCodeAsync() =>
        Assert.Equal(["ThrowingPolicy.cs"], await DomainPurityRules.ThrowAsync(Violating, Cancellation));

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void DomainResultsUseTheirOwnModuleErrors(Scope scope) =>
        Assert.Empty(DomainPurityRules.ReturnForeignErrors(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsResultsWithForeignErrors() =>
        Assert.Equal(["ForeignErrorLedger"], Violations.Named(DomainPurityRules.ReturnForeignErrors(Violating)));

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void EveryModuleHasExactlyOneErrorEnum(Scope scope) =>
        Assert.Empty(DomainPurityRules.ModulesWithoutASingleErrorEnum(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsModulesWithSeveralErrorEnums() =>
        Assert.Equal(["Avala.Fixtures.Violating"], DomainPurityRules.ModulesWithoutASingleErrorEnum(Violating));

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void StatelessStaysInTheDomain(Scope scope) =>
        Assert.Empty(DomainPurityRules.UseStatelessOutsideDomain(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsStatelessOutsideTheDomain() =>
        Assert.Equal(["StatelessCoordinator"], Violations.Named(DomainPurityRules.UseStatelessOutsideDomain(Violating)));
}
