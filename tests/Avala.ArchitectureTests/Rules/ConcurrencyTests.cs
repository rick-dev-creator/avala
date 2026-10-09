using Avala.ArchitectureTests.Concurrency;
using Avala.ArchitectureTests.Scopes;
using Avala.ArchitectureTests.Solution;

namespace Avala.ArchitectureTests.Rules;

public sealed class ConcurrencyTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProductionCodeCoordinatesThreadsOnlyThroughTheBusAndChannelsAsync() =>
        Assert.Empty(await CoordinationRules.CoordinateThreadsAsync(SolutionLayout.SourceDirectory, Cancellation));

    [Fact]
    public async Task AcceptsStateOwnedByOneReaderWithImmutableSnapshotsAsync() =>
        Assert.Empty(await CoordinationRules.CoordinateThreadsAsync(CodeScopes.Of(Scope.Compliant).SourceDirectory, Cancellation));

    [Fact]
    public async Task DetectsLocksSemaphoresMonitorsAndConcurrentCollectionsAsync() =>
        Assert.Equal(
            [
                "ContendedLedger: ConcurrentQueue",
                "ContendedLedger: Lock",
                "ContendedLedger: Monitor",
                "ContendedLedger: SemaphoreSlim",
                "ContendedLedger: lock",
                "ImpatientLedger: SpinWait",
            ],
            await CoordinationRules.CoordinateThreadsAsync(CodeScopes.Of(Scope.Violating).SourceDirectory, Cancellation));
}
