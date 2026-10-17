using Avala.Testing;
using Avala.Testing.UI;
using Avala.Workbench.NewJob;
using Avala.Workbench.Settings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Avala.Workbench.Tests.Views;

public sealed class DocsFeatureScreens(HeadlessUi ui)
{
    private const string Gate = "AVALA_FEATURE_SCREENS";

    private static string? Folder => Environment.GetEnvironmentVariable(Gate) is { Length: > 0 } found ? found : null;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public Task NewJobsFeatureChipAndItsMenuAsync(string variant) =>
        ShootAsync(() =>
        {
            var view = Show(new DesignNewJobViewModel(), variant, 1000, 900);
            Save(view, $"docs-new-job-chip-{variant}");
            view.Click("FeatureButton");
            Pump(view);
            Assert.True(Save(view, $"docs-new-job-chip-menu-{variant}"));
        });

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public Task TheRepositorysDocsSwitchAsync(string variant) =>
        ShootAsync(() =>
        {
            var view = Show(new DesignDocsSettingViewModel(), variant, 760, 360);
            Assert.True(Save(view, $"docs-settings-{variant}"));
        });

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public Task TheC4PlayerAtRestAndMidFlowAsync(string variant) =>
        ShootAsync(() =>
        {
            using var player = new Avala.Workbench.Docs.C4PlayerViewModel(TimeProvider.System, new InlineUiDispatcher());
            player.Show(Avala.Workbench.Docs.DesignStories.Ledger());
            var view = Show(player, variant, 1240, 720);
            Save(view, $"docs-player-{variant}");
            player.Version = 0;
            player.GoTo("a-charge");
            player.NextCommand.Execute(null);
            Pump(view);
            Assert.True(Save(view, $"docs-player-flow-{variant}"));
        });

    private Task ShootAsync(Action shoot)
    {
        Assert.SkipUnless(Folder is not null && HeadlessApp.Renders, $"Set {Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the documentation screens.");

        return ui.RunAsync(shoot, Cancellation);
    }

    private static ViewScript Show(object viewModel, string variant, int width, int height)
    {
        var view = Screen.Show(viewModel, variant == "light" ? ThemeVariant.Light : ThemeVariant.Dark, width, height);
        view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
        Components.UI.Theme.Motion.SetIsReduced(view.Window, true);
        Pump(view);

        return view;
    }

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
