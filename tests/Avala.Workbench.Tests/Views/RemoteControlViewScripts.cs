using Avala.Components.UI.QrCodes;
using Avala.Testing.UI;
using Avala.Workbench.Remoting;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class RemoteControlViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ThePhoneActionShowsTheLinkItsCodeAndTheConnectedDeviceAsync() =>
        ui.RunAsync(() =>
        {
            var view = Opened(new DesignRemoteControlViewModel { IsOpen = true });

            Assert.True(view.HasClass("Phone", "on"));
            Assert.True(view.Shows("Connected"));
            Assert.Equal(DesignRemoteControlViewModel.SessionLink, view.TextOf("Link"));
            Assert.False(view.Find<QrCode>("Code").Modules.IsEmpty);
            Assert.Equal((true, true, true), (view.Shows("Code"), view.Shows("DeviceRow"), view.Find<ToggleSwitch>("Toggle").IsChecked == true));
            Assert.False(view.Shows("Problem"));
        }, Cancellation);

    [Fact]
    public Task TheSwitchTurnsItOnOrOffAndTheLinkCanBeCopiedOrOpenedAsync() =>
        ui.RunAsync(() =>
        {
            var calls = new List<string>();
            var view = Opened(new DesignRemoteControlViewModel
            {
                IsOpen = true,
                ToggleCommand = Recording(calls, "toggle"),
                CopyLinkCommand = Recording(calls, "copy"),
                OpenLinkCommand = Recording(calls, "open"),
            });

            view.Click("Toggle").Click("CopyLink").Click("OpenLink");

            Assert.Equal(["toggle", "copy", "open"], calls);
        }, Cancellation);

    [Fact]
    public Task BeforeAnyLinkThePopoverShowsOnlyTheSwitchAndWhyItStoppedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Opened(new DesignRemoteControlViewModel { IsOpen = true, IsOn = false, IsConnected = false, Link = string.Empty, Status = RemotePhrases.Off, Problem = "Remote Control requires claude.ai subscription auth." });

            Assert.Equal((false, false, false), (view.Shows("Connect"), view.Shows("DeviceRow"), view.Shows("Connected")));
            Assert.Equal(("Remote Control requires claude.ai subscription auth.", RemotePhrases.Off), (view.TextOf("Problem"), view.TextOf("Status")));
        }, Cancellation);

    [Fact]
    public Task AJobThatCannotBeContinuedOnThePhoneShowsNoActionAsync() =>
        ui.RunAsync(() => Assert.False(Screen.Show(new DesignRemoteControlViewModel { IsShown = false }).Shows("Phone")), Cancellation);

    private static ViewScript Opened(DesignRemoteControlViewModel remote)
    {
        var view = Screen.Show(remote, ThemeVariant.Dark, 700, 600);
        view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });

        return view.Settle();
    }

    private static AsyncRelayCommand Recording(List<string> calls, string name) => new(() =>
    {
        calls.Add(name);

        return Task.CompletedTask;
    });
}
