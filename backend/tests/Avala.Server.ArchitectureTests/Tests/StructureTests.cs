using Avala.Server.ArchitectureTests.Layout;
using Avala.Server.ArchitectureTests.Rules;

namespace Avala.Server.ArchitectureTests.Tests;

public sealed class StructureTests
{
    private static ServerScope Production => ServerScopes.Of(Scope.Production);

    private static ServerScope Violating => ServerScopes.Of(Scope.Violating);

    [Fact]
    public void EveryModuleProjectIsOneOfTheSixLayers() =>
        Assert.Empty(StructureRules.ProjectsOutsideTheLayers(Production));

    [Fact]
    public void DetectsProjectsOutsideTheLayers() =>
        Assert.Equal(["Avala.Server.Violating.Helpers"], Violations.Sorted(StructureRules.ProjectsOutsideTheLayers(Violating)));

    [Fact]
    public void EveryModuleHasAllSixLayers() =>
        Assert.Empty(StructureRules.MissingLayers(Production));

    [Fact]
    public void DetectsModulesMissingLayers() =>
        Assert.Equal(
            ["Avala.Server.Partial.Application", "Avala.Server.Partial.Contracts", "Avala.Server.Partial.EndPoints", "Avala.Server.Partial.Infrastructure", "Avala.Server.Partial.Module"],
            Violations.Sorted(StructureRules.MissingLayers(Violating)));

    [Fact]
    public void TypesLiveInTheirProjectNamespace() =>
        Assert.Empty(StructureRules.TypesOutsideTheirProjectNamespace(Production));

    [Fact]
    public void DetectsTypesOutsideTheirProjectNamespace() =>
        Assert.Equal(["Avala.Server.Violating.Sales.SalesReport"], Violations.Sorted(StructureRules.TypesOutsideTheirProjectNamespace(Violating)));

    [Fact]
    public void EveryClassIsSealedOrStatic() =>
        Assert.Empty(StructureRules.ClassesNeitherSealedNorStatic(Production));

    [Fact]
    public void DetectsClassesOpenToInheritance() =>
        Assert.Equal(["Avala.Server.Violating.Domain.Ledger"], Violations.Sorted(StructureRules.ClassesNeitherSealedNorStatic(Violating)));
}
