using Avala.ArchitectureTests.Persistence;
using Avala.ArchitectureTests.Scopes;

namespace Avala.ArchitectureTests.Rules;

public sealed class PersistenceTests
{
    private static CodeScope Violating => CodeScopes.Of(Scope.Violating);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void OnlyInfrastructureUsesEntityFramework(Scope scope) =>
        Assert.Empty(PersistenceRules.UseEntityFrameworkOutsideInfrastructure(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsEntityFrameworkOutsideInfrastructure() =>
        Assert.Equal(
            ["EntityFrameworkAwareService"],
            Violations.Named(PersistenceRules.UseEntityFrameworkOutsideInfrastructure(Violating)));

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void EveryDatabaseHasMigrationsAndAModelSnapshot(Scope scope) =>
        Assert.Empty(PersistenceRules.DatabasesWithoutMigrations(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsADatabaseWithoutMigrations() =>
        Assert.Equal(["LedgerDbContext"], Violations.Named(PersistenceRules.DatabasesWithoutMigrations(Violating)));

    [Fact]
    public void EveryModuleDatabaseMatchesItsLatestMigration() =>
        Assert.Empty(PersistenceRules.DatabasesDriftedFromTheirMigrations(CodeScopes.Of(Scope.Production)));

    [Fact]
    public void DetectsADatabaseWhoseModelChangedWithoutAMigration() =>
        Assert.Equal(["DriftedLedgerDbContext"], Violations.Named(PersistenceRules.DatabasesDriftedFromTheirMigrations(Violating)));

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public async Task DatabaseWorkRunsOffTheCallingThreadAsync(Scope scope) =>
        Assert.Empty(await PersistenceRules.HoldContextsWithoutTaskRunAsync(CodeScopes.Of(scope), Cancellation));

    [Fact]
    public async Task DetectsStoresRunningDatabaseWorkOnTheCallingThreadAsync() =>
        Assert.Equal(
            ["BlockingLedgerStore"],
            Violations.Named(await PersistenceRules.HoldContextsWithoutTaskRunAsync(Violating, Cancellation)));
}
