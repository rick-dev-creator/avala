using Avala.Host.Composition;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class DataFolderClaimTests
{
    [Fact]
    public async Task AClaimedDataFolderRefusesAnotherClaimUntilTheFirstIsReleasedAsync()
    {
        await using var data = new TemporaryFolder();
        var paths = new AvalaPaths(Path.Combine(data.Path, "not-created-yet"));

        var first = Outcomes.Present(DataFolderClaim.TryTake(paths));
        var second = DataFolderClaim.TryTake(paths);
        first.Dispose();
        using var third = Outcomes.Present(DataFolderClaim.TryTake(paths));

        Assert.True(second.IsNone);
        Assert.True(File.Exists(Path.Combine(paths.Data, DataFolderClaim.Marker)));
    }

    [Fact]
    public async Task AnotherDataFolderCanBeClaimedWhileOneIsHeldAsync()
    {
        await using var data = new TemporaryFolder();
        var paths = new AvalaPaths(data.Path);
        using var running = Outcomes.Present(DataFolderClaim.TryTake(paths));
        await using var other = new TemporaryFolder();

        Assert.True(DataFolderClaim.TryTake(paths).IsNone);
        Assert.True(DataFolderClaim.TryTake(new AvalaPaths(other.Path)).Match(claim => { claim.Dispose(); return true; }, () => false));
    }
}
