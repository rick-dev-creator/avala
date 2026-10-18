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
    public Task ThePopoverExplainsTheHoldOnceUnderTheWindowsNearItAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageLeftViewModel(), ThemeVariant.Dark, 640, 900);
            view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
            var chip = view.Find<Button>("Open");

            ToolTip.SetIsOpen(chip, true);
            view.Settle();
            var detail = Assert.IsAssignableFrom<Control>(ToolTip.GetTip(chip));

            Assert.Equal(
                ["Jobs on this connection hold at the 90% threshold."],
                detail.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && text.Text?.Contains("hold at", StringComparison.Ordinal) == true).Select(text => text.Text));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AtTheSmallestWindowThePopoverOfThreeWindowsUsedOutsideAvalaOpensUnderItsChipAndFitsAsync() =>
        ui.RunAsync(() =>
        {
            var outside = UsagePhrases.Outside(0.12);
            var usage = new Chip(isShown: true)
            {
                HoldAt = "Jobs on this connection hold at the 98% threshold.",
                More = "2 more windows on the Usage page",
                Windows =
                [
                    new DesignLimitViewModel("5h", "5-hour window", 0.97, "97%", "resets Tue 20 Oct 18:45", 0.98, false) { Outside = outside },
                    new DesignLimitViewModel("7d", "7-day window", 0.92, "92%", "resets Mon 26 Oct 09:00", 0.98, false) { Outside = outside },
                    new DesignLimitViewModel("7d_opus", "7-day Opus window", 0.64, "64%", "resets Mon 26 Oct 09:00", 0.98, false) { Outside = outside },
                ],
            };
            var regions = new Avala.Workbench.Navigation.JobRegions(
                [
                    new Avala.Sdk.Regions.RegionContribution(Avala.Sdk.Regions.WorkbenchRegions.JobHeader, 2, new Avala.Workbench.Remoting.DesignRemoteControlViewModel()),
                    new Avala.Sdk.Regions.RegionContribution(Avala.Sdk.Regions.WorkbenchRegions.JobHeader, 5, usage),
                ],
                new Avala.Testing.RememberedLayout());
            var view = Screen.Show(new Avala.Workbench.Navigation.DesignWorkbenchViewModel { Regions = regions }, ThemeVariant.Dark, 720, 480);
            view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
            var chip = view.Find<Button>("Open");

            ToolTip.SetIsOpen(chip, true);
            view.Settle();
            var detail = Assert.IsAssignableFrom<Control>(ToolTip.GetTip(chip));
            var top = detail.TranslatePoint(default, view.Window)!.Value.Y;
            var bottom = detail.TranslatePoint(new Point(0, detail.Bounds.Height), view.Window)!.Value.Y;
            var under = chip.TranslatePoint(new Point(0, chip.Bounds.Height), view.Window)!.Value.Y;

            Assert.True(top >= under && bottom <= view.Window.Bounds.Height, $"the popover runs from {top:0} to {bottom:0} under a chip ending at {under:0} in a window {view.Window.Bounds.Height:0} high");
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task BesideAnOpenInspectorAtTheSmallestWindowThePopoverOfALongConnectionOpensUnderItsChipAndFitsAsync() =>
        ui.RunAsync(() =>
        {
            var outside = UsagePhrases.Outside(0.12);
            var usage = new Chip(isShown: true, "claude-work-account-for-the-payments-platform-team")
            {
                HoldAt = "Jobs on this connection hold at the 98% threshold.",
                More = "2 more windows on the Usage page",
                Outside = UsageLeftPhrases.Outside,
                Windows =
                [
                    new DesignLimitViewModel("5h", "5-hour window", 0.97, "97%", "resets Tue 20 Oct 18:45", 0.98, false) { Outside = outside },
                    new DesignLimitViewModel("7d", "7-day window", 0.92, "92%", "resets Mon 26 Oct 09:00", 0.98, false) { Outside = outside },
                    new DesignLimitViewModel("7d_opus", "7-day Opus window", 0.64, "64%", "resets Mon 26 Oct 09:00", 0.98, false) { Outside = outside },
                ],
            };
            var regions = new Avala.Workbench.Navigation.JobRegions(
                [
                    new Avala.Sdk.Regions.RegionContribution(Avala.Sdk.Regions.WorkbenchRegions.JobHeader, 2, new Avala.Workbench.Remoting.DesignRemoteControlViewModel()),
                    new Avala.Sdk.Regions.RegionContribution(Avala.Sdk.Regions.WorkbenchRegions.JobHeader, 4, new DesignContextLeftViewModel()),
                    new Avala.Sdk.Regions.RegionContribution(Avala.Sdk.Regions.WorkbenchRegions.JobHeader, 5, usage),
                ],
                new Avala.Testing.RememberedLayout());
            var view = Screen.Show(new Avala.Workbench.Navigation.DesignWorkbenchViewModel { Regions = regions }, ThemeVariant.Dark, 400, 480);
            view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
            var chip = view.All<Button>().First(button => button.Name == "Open" && button.GetVisualAncestors().Any(parent => parent.GetType().Name == "UsageLeftView"));

            ToolTip.SetIsOpen(chip, true);
            view.Settle();
            var detail = Assert.IsAssignableFrom<Control>(ToolTip.GetTip(chip));
            var top = detail.TranslatePoint(default, view.Window)!.Value.Y;
            var bottom = detail.TranslatePoint(new Point(0, detail.Bounds.Height), view.Window)!.Value.Y;
            var under = chip.TranslatePoint(new Point(0, chip.Bounds.Height), view.Window)!.Value.Y;

            Assert.True(top >= under && bottom <= view.Window.Bounds.Height, $"the popover runs from {top:0} to {bottom:0} under a chip ending at {under:0} in a window {view.Window.Bounds.Height:0} high");
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ThePopoverSaysItsWindowsAreSharedWithEveryAppOnTheAccountAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageLeftViewModel(), ThemeVariant.Dark, 640, 900);
            view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
            var chip = view.Find<Button>("Open");

            ToolTip.SetIsOpen(chip, true);
            view.Settle();

            Assert.Contains(
                Assert.IsAssignableFrom<Control>(ToolTip.GetTip(chip)).GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text),
                text => text?.StartsWith("Shared with every app on this account, not only Avala.", StringComparison.Ordinal) == true);
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

        public string HoldAt { get; init; } = string.Empty;

        public string More { get; init; } = string.Empty;

        public string Outside { get; init; } = string.Empty;

        public IReadOnlyList<ILimitViewModel> Windows { get; init; } = [];

        public IRelayCommand OpenCommand { get; }
    }
}
