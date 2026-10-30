using Avala.Server.ArchitectureTests.Layout;
using Avala.Server.ArchitectureTests.Rules;

namespace Avala.Server.ArchitectureTests.Tests;

public sealed class LayerTests
{
    private static ServerScope Production => ServerScopes.Of(Scope.Production);

    private static ServerScope Violating => ServerScopes.Of(Scope.Violating);

    [Fact]
    public void EachLayerReferencesOnlyWhatItsLayerAllows() =>
        Assert.Empty(ReferenceRules.ReferencesOutsideTheirLayer(Production));

    [Fact]
    public void DetectsReferencesAgainstTheLayers() =>
        Assert.Equal(
            [
                "Avala.Server.Violating.Application -> Avala.Server.Violating.Infrastructure",
                "Avala.Server.Violating.Application -> Microsoft.EntityFrameworkCore",
                "Avala.Server.Violating.Application -> Microsoft.EntityFrameworkCore.Relational",
                "Avala.Server.Violating.Domain -> Microsoft.EntityFrameworkCore.Abstractions",
                "Avala.Server.Violating.EndPoints -> Avala.Server.Violating.Domain",
                "Avala.Server.Violating.Module -> FastEndpoints",
                "Avala.Server.Violating.Module -> FastEndpoints.Messaging.Core",
            ],
            Violations.Sorted(ReferenceRules.ReferencesOutsideTheirLayer(Violating)));

    [Fact]
    public void OnlyTheModuleInstallerIsPublic() =>
        Assert.Empty(VisibilityRules.PublicTypesBesidesTheEntry(Production));

    [Fact]
    public void DetectsPublicTypesBesidesTheModuleInstaller() =>
        Assert.Equal(
            ["Avala.Server.Violating.Domain.Order", "Avala.Server.Violating.Module.ViolatingSettings"],
            Violations.Sorted(VisibilityRules.PublicTypesBesidesTheEntry(Violating)));

    [Fact]
    public void InternalsAreVisibleOnlyAlongTheLayers() =>
        Assert.Empty(VisibilityRules.InternalsVisibleAgainstTheLayers(Production));

    [Fact]
    public void DetectsInternalsVisibleAgainstTheLayers() =>
        Assert.Equal(
            ["Avala.Server.Violating.Domain -> Avala.Server.Violating.EndPoints", "Avala.Server.Violating.Infrastructure -> Avala.Server.Violating.Application"],
            Violations.Sorted(VisibilityRules.InternalsVisibleAgainstTheLayers(Violating)));

    [Fact]
    public void TheDomainIsPureCSharp() =>
        Assert.Empty(DomainRules.DependOnMoreThanPureCSharp(Production));

    [Fact]
    public void DetectsDomainTypesDependingOnPersistenceIoOrOtherLayers() =>
        Assert.Equal(
            ["Avala.Server.Violating.Domain.Ledger", "Avala.Server.Violating.Domain.Order"],
            Violations.Sorted(DomainRules.DependOnMoreThanPureCSharp(Violating)));
}
