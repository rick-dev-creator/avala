using Avala.Preview.Pane;
using Avala.Testing.UI;
using Avalonia.Controls;

namespace Avala.Preview.Tests.Views;

public sealed class DeviceFrameViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheDeviceMenuListsResponsiveAndEveryDeviceWithTheChosenOneCheckedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DeviceFrameViewModel());
            var device = view.Find<Button>("Device");
            var menu = Assert.IsType<MenuFlyout>(device.Flyout);

            menu.ShowAt(device);
            view.Settle();
            var items = view.All<MenuItem>();

            Assert.Equal(["Responsive", "iPhone", "Android", "Tablet"], items.Select(item => item.Header));
            Assert.Equal([true, false, false, false], items.Select(item => item.IsChecked));
        }, Cancellation);

    [Fact]
    public Task ChoosingADeviceFromTheMenuOffersToTurnItAsync() =>
        ui.RunAsync(() =>
        {
            var frame = new DeviceFrameViewModel();
            var view = Screen.Show(frame);
            var device = view.Find<Button>("Device");
            Assert.False(view.Shows("Turn"));

            ((MenuFlyout)device.Flyout!).ShowAt(device);
            view.Settle();
            view.Click(view.All<MenuItem>().Single(item => Equals(item.Header, "Android")));

            Assert.Equal("Android · 412 × 915", frame.Label);
            Assert.True(view.Shows("Turn"));
        }, Cancellation);

    [Fact]
    public Task TurningFromTheToolbarSwapsTheDevicesSidesAsync() =>
        ui.RunAsync(() =>
        {
            var frame = new DeviceFrameViewModel();
            frame.Choices.Single(choice => choice.Name == "iPhone").Choose.Execute(null);
            var view = Screen.Show(frame);

            view.Click("Turn");

            Assert.Equal((852d, 393d), (frame.FrameWidth, frame.FrameHeight));
        }, Cancellation);

    [Fact]
    public Task TheDesignTimeFrameShowsAChosenPhoneAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDeviceFrameViewModel());

            Assert.True(view.Shows("Turn"));
            Assert.Equal("Device: iPhone · 393 × 852", ToolTip.GetTip(view.Find("Device")));
        }, Cancellation);
}
