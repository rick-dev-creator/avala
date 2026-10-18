using Avala.Testing.UI;
using Avala.Workbench.Usage;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class AdversaryPopoverScreens(HeadlessUi ui)
{
    private static string? Folder => Environment.GetEnvironmentVariable("AVALA_FEATURE_SCREENS") is { Length: > 0 } found ? found : null;

    public static TheoryData<string, string, double, double> Shots
    {
        get
        {
            var data = new TheoryData<string, string, double, double>();

            foreach (var shape in new[] { "long-name", "one-window", "many-windows", "full-and-expired", "empty-zero", "no-windows" })
            {
                foreach (var variant in new[] { "dark", "light" })
                {
                    data.Add(shape, variant, 720, 480);
                    data.Add(shape, variant, 1920, 1080);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Shots))]
    public Task UsagePopoverShapesAsync(string shape, string variant, double width, double height)
    {
        Assert.SkipUnless(Folder is not null && HeadlessApp.Renders, "screens");

        return ui.RunAsync(() =>
        {
            var view = Screen.Show(Shape(shape), variant == "light" ? ThemeVariant.Light : ThemeVariant.Dark, width, height);
            view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
            Components.UI.Theme.Motion.SetIsReduced(view.Window, true);
            var control = view.Window.GetVisualDescendants().OfType<UserControl>().First(found => found.GetType().Name == "UsageLeftView");
            control.HorizontalAlignment = HorizontalAlignment.Right;
            control.VerticalAlignment = VerticalAlignment.Top;
            control.Margin = new Avalonia.Thickness(0, 12, 8, 0);
            Pump(view);
            ToolTip.SetIsOpen(view.Find<Button>("Open"), true);
            Pump(view);
            var path = Path.Combine(Directory.CreateDirectory(Folder ?? ".").FullName, $"usage-{shape}-{width}-{variant}.png");
            view.Window.CaptureRenderedFrame()?.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public Task ProjectMenuWithDocsOnAndAShortcutBesideALongItemAsync(string variant)
    {
        Assert.SkipUnless(Folder is not null && HeadlessApp.Renders, "screens");

        return ui.RunAsync(() =>
        {
            var theme = variant == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var view = Screen.Show(new Avala.Workbench.Sidebar.DesignProjectGroupViewModel(), theme, 640, 400);
            view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
            var header = view.Find<Button>("Header");
            ((MenuFlyout)header.ContextFlyout!).ShowAt(header);
            Pump(view);
            view.Window.CaptureRenderedFrame()?.Save(Path.Combine(Directory.CreateDirectory(Folder ?? ".").FullName, $"project-menu-docs-on-{variant}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

            var owner = new Border { Width = 200, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var other = ViewScript.Show(new Panel { Children = { owner } }, new object());
            other.Window.RequestedThemeVariant = theme;
            other.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
            var menu = new MenuFlyout
            {
                Items =
                {
                    new MenuItem { Header = "Copy the path of the payments platform migration worktree", InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.C, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift) },
                    new MenuItem { Header = "Select All", InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.A, Avalonia.Input.KeyModifiers.Control) },
                },
            };
            menu.ShowAt(owner);
            Pump(other);
            other.Window.CaptureRenderedFrame()?.Save(Path.Combine(Directory.CreateDirectory(Folder ?? ".").FullName, $"shortcut-beside-long-item-{variant}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }, TestContext.Current.CancellationToken);
    }

    private static Fake Shape(string shape) => shape switch
    {
        "long-name" => new Fake("claude-work-account-for-the-payments-platform-team-in-frankfurt", "57% usage left", 0.43, false,
        [
            new DesignLimitViewModel("5h", "5-hour window", 0.43, "43%", "resets Tue 20 Oct 18:45", 0.98, false),
            new DesignLimitViewModel("7d", "7-day window", 0.21, "21%", "resets Mon 26 Oct 09:00", 0.98, false),
        ]),
        "one-window" => new Fake("claude-work", "69% usage left", 0.31, false, [new DesignLimitViewModel("usage", "Usage window", 0.31, "31%", "no reset reported", 0, false)]),
        "many-windows" => new Fake("claude-work", "3% usage left", 0.97, true,
        [
            new DesignLimitViewModel("5h", "5-hour window", 0.97, "97%", "resets Tue 20 Oct 18:45", 0.98, false) { Outside = "61% of it was spent outside Avala." },
            new DesignLimitViewModel("7d", "7-day window", 0.92, "92%", "resets Mon 26 Oct 09:00", 0.98, false),
            new DesignLimitViewModel("7d-opus", "7-day Opus window with a very long label from the provider", 0.64, "64%", "resets Mon 26 Oct 09:00", 0.98, false),
            new DesignLimitViewModel("7d-sonnet", "7-day Sonnet window", 0.12, "12%", "resets Mon 26 Oct 09:00", 0.98, false),
            new DesignLimitViewModel("month", "Monthly window", 0.55, "55%", "no reset reported", 0.98, false),
        ]),
        "full-and-expired" => new Fake("claude-personal", "0% usage left", 1, true,
        [
            new DesignLimitViewModel("5h", "5-hour window", 1, "100%", "resets Tue 20 Oct 18:45", 0.98, true),
            new DesignLimitViewModel("7d", "7-day window", 0, "reset", "reset Tue 20 Oct 12:00, no reading since", 0.98, false),
        ]),
        "empty-zero" => new Fake("claude-work", "100% usage left", 0, false,
        [
            new DesignLimitViewModel("5h", "5-hour window", 0, "0%", "no reset reported", 0, false),
        ]),
        _ => new Fake("claude-work", string.Empty, 0, false, []),
    };

    private static void Pump(ViewScript view)
    {
        for (var frame = 0; frame < 30; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        view.Settle();
    }

    private sealed class Fake(string connection, string left, double used, bool near, IReadOnlyList<ILimitViewModel> windows) : IUsageLeftViewModel
    {
        public bool IsShown => true;

        public string Connection => connection;

        public string Left => left;

        public double Used => used;

        public bool IsNear => near;

        public string Summary => "summary";

        public string Shared => UsagePhrases.Shared(connection);

        public IReadOnlyList<ILimitViewModel> Windows => windows;

        public IRelayCommand OpenCommand { get; } = new RelayCommand(() => { });
    }
}
