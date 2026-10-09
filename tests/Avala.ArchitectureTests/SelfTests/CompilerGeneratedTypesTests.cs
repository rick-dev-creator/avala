using Avala.ArchitectureTests.Scopes;

namespace Avala.ArchitectureTests.SelfTests;

public sealed class CompilerGeneratedTypesTests
{
    [Fact]
    public void TheCompliantFixtureDependsOnATypeTheCompilerGeneratesWithoutANamespaceSoEveryDependencyRuleMeetsOne() =>
        Assert.True(CodeScopes.Of(Scope.Compliant).ArchitectureTypes.Single(type => type.Name == "Money").DependsOnCompilerGeneratedTypes);
}
