using Avala.Server.ArchitectureTests.Layout;
using Avala.Server.ArchitectureTests.Rules;

namespace Avala.Server.ArchitectureTests.Tests;

public sealed class PersistenceTests
{
    private static ServerScope Production => ServerScopes.Of(Scope.Production);

    private static ServerScope Violating => ServerScopes.Of(Scope.Violating);

    [Fact]
    public void DbContextsLiveInInfrastructure() =>
        Assert.Empty(PersistenceRules.DbContextsOutsideInfrastructure(Production));

    [Fact]
    public void DetectsDbContextsOutsideInfrastructure() =>
        Assert.Equal(["Avala.Server.Violating.Application.ReminderDbContext"], Violations.Sorted(PersistenceRules.DbContextsOutsideInfrastructure(Violating)));

    [Fact]
    public void EachModuleHasAtMostOneDbContext() =>
        Assert.Empty(PersistenceRules.ModulesWithSeveralDbContexts(Production));

    [Fact]
    public void DetectsModulesWithSeveralDbContexts() =>
        Assert.Equal(["Violating"], Violations.Sorted(PersistenceRules.ModulesWithSeveralDbContexts(Violating)));

    [Fact]
    public void EachDbContextOwnsItsModuleSchema() =>
        Assert.Empty(PersistenceRules.DbContextsOutsideTheirModuleSchema(Production));

    [Fact]
    public void DetectsDbContextsOutsideTheirModuleSchema() =>
        Assert.Equal(["OrdersDbContext: sales"], Violations.Sorted(PersistenceRules.DbContextsOutsideTheirModuleSchema(Violating)));

    [Fact]
    public void TheDomainIsWhatIsPersisted() =>
        Assert.Empty(PersistenceRules.EntitiesOutsideTheDomain(Production));

    [Fact]
    public void DetectsPersistenceModelsBesideTheDomain() =>
        Assert.Equal(["OrdersDbContext -> OrderRow"], Violations.Sorted(PersistenceRules.EntitiesOutsideTheDomain(Violating)));
}
