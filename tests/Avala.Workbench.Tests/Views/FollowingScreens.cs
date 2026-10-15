using Avala.Testing.UI;
using Avala.Workbench.Overview;
using Avala.Workbench.Remoting;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Avala.Workbench.Tests.Views;

public sealed class FollowingScreens(HeadlessUi ui)
{
    private const string Gate = "AVALA_FEATURE_SCREENS";

    private static string? Folder => Environment.GetEnvironmentVariable(Gate) is { Length: > 0 } found ? found : null;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public Task ContinueOnYourPhoneAsync(string variant) =>
        ShootAsync(() =>
        {
            var view = Screen.Show(new DesignRemoteControlViewModel { IsOpen = true }, Theme(variant), 420, 480);
            view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
            Components.UI.Theme.Motion.SetIsReduced(view.Window, true);
            Pump(view);
            Assert.True(Save(view, $"remote-control-{variant}"));
        });

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public Task TheDelegationPaneAsync(string variant) =>
        ShootAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationPaneViewModel(), Theme(variant), 420, 860);
            Components.UI.Theme.Motion.SetIsReduced(view.Window, true);
            Pump(view);
            Assert.True(Save(view, $"delegation-pane-{variant}"));
        });

    private Task ShootAsync(Action shoot)
    {
        Assert.SkipUnless(Folder is not null && HeadlessApp.Renders, $"Set {Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the following screens.");

        return ui.RunAsync(shoot, Cancellation);
    }

    private static ThemeVariant Theme(string variant) => variant == "light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private static void Pump(ViewScript view)
    {
        for (var frame = 0; frame < 30; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        view.Settle();
    }

    private static bool Save(ViewScript view, string name)
    {
        var path = Path.Combine(Directory.CreateDirectory(Folder!).FullName, $"{name}.png");
        view.Window.CaptureRenderedFrame()?.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return File.Exists(path);
    }
}
