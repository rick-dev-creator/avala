using Avala.Preview.Navigation;

namespace Avala.Preview.Tests.Navigation;

public sealed class DeviceViewTests
{
    [Fact]
    public void ADeviceInThreeDimensionsNamesItsModelSizeTurnAndThePageItHolds() =>
        Assert.Equal(
            new Uri("http://127.0.0.1:24009/__avala/viewer/device.html?device=surface-duo&w=720&h=1114&turned=1&page=http%3A%2F%2F127.0.0.1%3A34000%2Forders%3Fpage%3D2&v=5"),
            new DeviceView("surface-duo", 720, 1114, true).Around(new Uri("http://127.0.0.1:24009/__avala/viewer/device.html"), new Uri("http://127.0.0.1:34000/orders?page=2"), 5));
}
