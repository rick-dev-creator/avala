using Avala.ArchitectureTests.Ddd;
using Avala.ArchitectureTests.Scopes;
using Avala.ArchitectureTests.Solution;

namespace Avala.ArchitectureTests.Rules;

public sealed class NullabilityTests
{
    [Fact]
    public void SignaturesExposeNoNullables() =>
        Assert.Empty(NullabilityRules.ExposeNullables(AvalaAssemblies.AllTypes));

    [Fact]
    public void AcceptsSignaturesWithoutNullables() =>
        Assert.Empty(NullabilityRules.ExposeNullables(CodeScopes.Of(Scope.Compliant).Types));

    [Fact]
    public void DetectsNullablePropertiesAndMethods() =>
        Assert.Equal(
            [
                "Avala.Fixtures.Violating.Application.NullableLookup.DescribeAsync",
                "Avala.Fixtures.Violating.Application.NullableLookup.Find",
                "Avala.Fixtures.Violating.Application.NullableLookup.Name",
            ],
            NullabilityRules.ExposeNullables(CodeScopes.Of(Scope.Violating).Types).Order(StringComparer.Ordinal));
}
