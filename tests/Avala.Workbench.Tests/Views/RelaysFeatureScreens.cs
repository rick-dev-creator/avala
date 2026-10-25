using Avala.Testing;
using Avala.Testing.UI;
using Avala.Workbench.Overview;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Avala.Workbench.Tests.Views;

public sealed class RelaysFeatureScreens(HeadlessUi ui)
{
    private const string Gate = "AVALA_FEATURE_SCREENS";

    private static string? Folder => Environment.GetEnvironmentVariable(Gate) is { Length: > 0 } found ? found : null;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public Task TheRelaysPaneRunningAndWaitingForAResetAsync(string variant) =>
        ShootAsync(() =>
        {
            Assert.True(Save(Show(new DesignRelaysPaneViewModel(), variant), $"relays-running-{variant}"));
            Assert.True(Save(Show(new DesignRelaysPaneViewModel(waiting: true), variant), $"relays-waiting-{variant}"));
        });

    private Task ShootAsync(Action shoot)
    {
        Assert.SkipUnless(Folder is not null && HeadlessApp.Renders, $"Set {Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the relays screens.");

        return ui.RunAsync(shoot, Cancellation);
    }

    private static ViewScript Show(object viewModel, string variant)
    {
        var view = Screen.Show(viewModel, variant == "light" ? ThemeVariant.Light : ThemeVariant.Dark, 440, 860);
        Components.UI.Theme.Motion.SetIsReduced(view.Window, true);

        for (var frame = 0; frame < 30; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        view.Settle();

        return view;
    }

    private static bool Save(ViewScript view, string name)
    {
        var path = Path.Combine(Directory.CreateDirectory(Folder!).FullName, $"{name}.png");
        view.Window.CaptureRenderedFrame()?.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return File.Exists(path);
    }
}
