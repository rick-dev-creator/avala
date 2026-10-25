using Avala.Server.ArchitectureTests.Layout;
using Avala.Server.ArchitectureTests.Rules;

namespace Avala.Server.ArchitectureTests.Tests;

public sealed class CompositionTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheHostReferencesOnlyModuleEntriesAsync() =>
        Assert.Empty(await CompositionRules.HostReferencesBesidesModuleEntriesAsync(Cancellation));

    [Fact]
    public async Task TheHostComposesEveryModuleAsync() =>
        Assert.Empty(await CompositionRules.ModulesTheHostLeavesOutAsync(ServerScopes.Of(Scope.Production), Cancellation));
}
