using Avala.Preview.Navigation;

namespace Avala.Preview.Tests.Navigation;

public sealed class DeviceViewTests
{
    [Fact]
    public void ADeviceInThreeDimensionsNamesItsModelSizeTurnAndThePageItHolds() =>
        Assert.Equal(
            new Uri("http://127.0.0.1:24009/__avala/viewer/device.html?device=iphone-duo&w=548&h=772&turned=1&page=http%3A%2F%2F127.0.0.1%3A34000%2Forders%3Fpage%3D2&v=5"),
            new DeviceView("iphone-duo", 548, 772, true, true).Around(new Uri("http://127.0.0.1:24009/__avala/viewer/device.html"), new Uri("http://127.0.0.1:34000/orders?page=2"), 5));

    [Fact]
    public void AFlatDeviceShowsThePageOnTheScreenPage() =>
        Assert.Equal(
            ("__avala/viewer/screen.html", "__avala/viewer/device.html"),
            (new DeviceView("iphone-16", 852, 393, true, false).Page, new DeviceView("iphone-16", 852, 393, true, true).Page));
}
