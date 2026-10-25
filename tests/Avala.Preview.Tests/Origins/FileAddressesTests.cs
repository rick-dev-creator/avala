using Avala.Preview.Origins;

namespace Avala.Preview.Tests.Origins;

public sealed class FileAddressesTests
{
    private static readonly Uri Files = new("http://127.0.0.1:24009/");

    [Theory]
    [InlineData("models/robot.glb")]
    [InlineData("parts/bracket.STL")]
    [InlineData("scene.gltf")]
    [InlineData("chair.obj")]
    public void AModelOpensInTheViewerNamingItsPathAndVersion(string path) =>
        Assert.Equal(
            new Uri($"http://127.0.0.1:24009/__avala/viewer/index.html?model={Uri.EscapeDataString(path)}&v=3"),
            FileAddresses.Viewed(FileAddresses.Served(Files, path), 3));

    [Fact]
    public void APageOpensAtItsOwnAddressWithItsVersion() =>
        Assert.Equal(new Uri("http://127.0.0.1:24009/site/sistema%20solar.html?v=2"), FileAddresses.Viewed(FileAddresses.Served(Files, "site/sistema solar.html"), 2));

    [Fact]
    public void AServedPathKeepsItsFoldersWhateverTheirSeparator() =>
        Assert.Equal(new Uri("http://127.0.0.1:24009/models/a%23b/robot.glb"), FileAddresses.Served(Files, "models\\a#b\\robot.glb"));
}
