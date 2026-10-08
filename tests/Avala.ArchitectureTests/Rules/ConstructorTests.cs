using Avala.ArchitectureTests.Ddd;
using Avala.ArchitectureTests.Scopes;
using Avala.ArchitectureTests.Solution;

namespace Avala.ArchitectureTests.Rules;

public sealed class ConstructorTests
{
    [Fact]
    public void NoClassTakesMoreThanFourDependencies() =>
        Assert.Empty(ConstructorRules.TakeTooManyDependencies(AvalaAssemblies.AllTypes));

    [Fact]
    public void AcceptsClassesWithFewDependencies() =>
        Assert.Empty(ConstructorRules.TakeTooManyDependencies(CodeScopes.Of(Scope.Compliant).Types));

    [Fact]
    public void DetectsClassesWithTooManyDependencies() =>
        Assert.Equal(
            ["OverloadedService"],
            Violations.Named(ConstructorRules.TakeTooManyDependencies(CodeScopes.Of(Scope.Violating).Types)));
}
