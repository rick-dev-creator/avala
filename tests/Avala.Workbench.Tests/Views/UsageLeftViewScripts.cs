using Avala.Testing.UI;
using Avala.Workbench.Usage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class UsageLeftViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheChipShowsWhatIsLeftAndWarnsNearTheLimitAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageLeftViewModel());

            Assert.Equal(("12% usage left", true, true, true), (view.TextOf("Left"), view.Shows("Open"), view.HasClass("Left", "attention"), view.HasClass("Ring", "attention")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ClickingTheChipOpensUsageAsync() =>
        ui.RunAsync(() =>
        {
            var chip = new Chip(isShown: true);
            var view = Screen.Show(chip);

            view.Click("Open");

            Assert.Equal(1, chip.Opened);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task HoveringTheChipShowsTheConnectionAndEachWindowBelowItAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageLeftViewModel(), ThemeVariant.Dark, 640, 900);
            view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
            var chip = view.Find<Button>("Open");

            ToolTip.SetIsOpen(chip, true);
            view.Settle();
            var detail = Assert.IsAssignableFrom<Control>(ToolTip.GetTip(chip));
            string? Text(string name) => detail.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == name).Text;

            Assert.Equal(("claude-work", "12% usage left"), (Text("Connection"), Text("LeftDetail")));
            Assert.Equal(2, detail.GetVisualDescendants().Count(control => control.GetType().Name == "LimitView"));
            Assert.True(detail.TranslatePoint(default, view.Window)!.Value.Y >= chip.TranslatePoint(new Point(0, chip.Bounds.Height), view.Window)!.Value.Y);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ALongConnectionNameIsTrimmedToOneLineAtTheTopOfThePopoverAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new Chip(isShown: true, "claude-work-account-for-the-payments-platform-team-in-frankfurt"), ThemeVariant.Dark, 640, 900);
            view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
            var chip = view.Find<Button>("Open");

            ToolTip.SetIsOpen(chip, true);
            view.Settle();
            var connection = Assert.IsAssignableFrom<Control>(ToolTip.GetTip(chip)).GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "Connection");

            Assert.Equal((1, true), (connection.TextLayout.TextLines.Count, connection.TextLayout.TextLines[0].HasCollapsed));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task WithoutAWindowToShowTheChipIsHiddenAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new Chip(isShown: false));

            Assert.False(view.Shows("Open"));
        }, TestContext.Current.CancellationToken);

    private sealed class Chip : IUsageLeftViewModel
    {
        public Chip(bool isShown, string connection = "claude-work")
        {
            IsShown = isShown;
            Connection = connection;
            OpenCommand = new RelayCommand(() => Opened++);
        }

        public int Opened { get; private set; }

        public bool IsShown { get; }

        public string Connection { get; }

        public string Left => "28% usage left";

        public double Used => 0.72;

        public bool IsNear => false;

        public string Summary => "Usage left on claude-work";

        public string Shared => "Shared with every app signed in to claude-work, not only Avala.";

        public IReadOnlyList<ILimitViewModel> Windows => [];

        public IRelayCommand OpenCommand { get; }
    }
}
