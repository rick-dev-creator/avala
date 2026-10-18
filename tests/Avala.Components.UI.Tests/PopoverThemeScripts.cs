using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Avala.Components.UI.Tests;

public sealed class PopoverThemeScripts(HeadlessUi ui)
{
    private const string Long = "Payments platform migration to the ledger service and the new invoice exporter, kept beside the old one until every bank export reads the same in both of them";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AHighlightedMenuItemTurnsItsTextAndIconToTheAccentsForegroundAsync() =>
        ui.RunAsync(() =>
        {
            var icon = new Avalonia.Controls.Shapes.Path { Classes = { "icon" } };
            var rename = new MenuItem { Header = "Rename", Icon = icon };
            var view = Opened(rename, new MenuItem { Header = "Archive" });

            view.Press(Avalonia.Input.Key.Down);
            view.Settle();

            Assert.Equal((Brush(view, "AccentBrush"), Brush(view, "OnAccentBrush"), Brush(view, "OnAccentBrush")), (Color(Part<Border>(rename, "PART_LayoutRoot").Background), Color(Part<ContentPresenter>(rename, "PART_HeaderPresenter").Foreground), Color(icon.Stroke)));
        }, Cancellation);

    [Fact]
    public Task AMenuJustOpenedHighlightsNoItemUntilThePointerOrTheKeyboardReachesOneAsync() =>
        ui.RunAsync(() =>
        {
            var rename = new MenuItem { Header = "Rename" };
            var archive = new MenuItem { Header = "Archive" };
            var view = Opened(rename, archive);

            Assert.DoesNotContain(Brush(view, "AccentBrush"), new[] { rename, archive }.Select(item => Color(Part<Border>(item, "PART_LayoutRoot").Background)));
        }, Cancellation);

    [Fact]
    public Task ACheckedItemKeepsTheIconColumnWhereEveryOtherItemHasItAsync() =>
        ui.RunAsync(() =>
        {
            var rename = new MenuItem { Header = "Rename", Icon = new Avalonia.Controls.Shapes.Path { Classes = { "icon" } } };
            var docs = new MenuItem { Header = "Document Features", ToggleType = MenuItemToggleType.CheckBox, IsChecked = true, Icon = new Avalonia.Controls.Shapes.Path { Classes = { "icon" } } };
            var view = Opened(rename, docs);

            Assert.Equal(Part<ContentControl>(rename, "PART_IconPresenter").TranslatePoint(default, view.Window)!.Value.X, Part<ContentControl>(docs, "PART_IconPresenter").TranslatePoint(default, view.Window)!.Value.X);
            Assert.Equal(0d, Part<ContentControl>(docs, "PART_ToggleIconPresenter").Bounds.Width);
        }, Cancellation);

    [Fact]
    public Task AMenuItemLongerThanTheMenuIsTrimmedWithinItsWidestWidthAsync() =>
        ui.RunAsync(() =>
        {
            var item = new MenuItem { Header = Long };
            var view = Opened(item);

            Assert.Equal((true, true), (Presenter(view).Bounds.Width <= Width(view, "MenuMaxWidth"), Header(item).TextLayout.TextLines.Any(line => line.HasCollapsed)));
        }, Cancellation);

    [Fact]
    public Task AnItemWithoutASubmenuReservesNoRoomForAShortcutOrAChevronAsync() =>
        ui.RunAsync(() =>
        {
            var leaf = new MenuItem { Header = "Archive" };
            var parent = new MenuItem { Header = "Move to", Items = { new MenuItem { Header = "Side projects" } } };
            _ = Opened(leaf, parent);

            Assert.Equal((0d, 0d), (Part<TextBlock>(leaf, "PART_InputGestureText").Margin.Left, Part<Avalonia.Controls.Shapes.Path>(leaf, "PART_ChevronPath").Margin.Left));
            Assert.True(Part<Avalonia.Controls.Shapes.Path>(parent, "PART_ChevronPath").Bounds.Width > 0);
        }, Cancellation);

    [Fact]
    public Task ASubmenuSharesTheMenusSurfaceRadiusAndWidestWidthAsync() =>
        ui.RunAsync(() =>
        {
            var parent = new MenuItem { Header = "Move to", Items = { new MenuItem { Header = Long }, new MenuItem { Header = "New project…" } } };
            var view = Opened(parent);

            parent.IsSubMenuOpen = true;
            view.Settle();
            var surface = Assert.IsType<Border>(Part<Popup>(parent, "PART_Popup").Child);

            Assert.Equal((Brush(view, "SurfacePopoverBrush"), view.Window.FindResource("RadiusRow"), true), (Color(surface.Background), (object?)surface.CornerRadius, surface.Bounds.Width <= Width(view, "MenuMaxWidth")));
        }, Cancellation);

    [Fact]
    public Task ADestructiveMenuItemIsDrawnInTheFailureColorAsync() =>
        ui.RunAsync(() =>
        {
            var delete = new MenuItem { Header = "Delete Project", Classes = { "destructive" } };
            var view = Opened(new MenuItem { Header = "Rename" }, delete);

            Assert.Equal(Brush(view, "FailureBrush"), Color(Part<ContentPresenter>(delete, "PART_HeaderPresenter").Foreground));
        }, Cancellation);

    [Fact]
    public Task ATooltipOpensAtOnceOnThePopoverSurfaceUnderReducedMotionAsync() =>
        ui.RunAsync(() =>
        {
            var (view, tip) = Hovered(new TextBlock { Text = "Inspector" });

            Assert.Equal((1d, Brush(view, "SurfacePopoverBrush")), (tip.Opacity, Color(tip.Background)));
        }, Cancellation);

    [Fact]
    public Task ATooltipHoldsTheWidestRichContentWithoutClippingItAsync() =>
        ui.RunAsync(() =>
        {
            var content = new Border { Height = 40, Width = 320 };
            var (_, tip) = Hovered(content);

            Assert.Equal((false, true), (content.Bounds.X < 0, content.Bounds.Width > 0 && content.TranslatePoint(new Point(content.Bounds.Width, 0), tip)!.Value.X <= tip.Bounds.Width));
        }, Cancellation);

    [Fact]
    public Task ADisabledDestructiveMenuItemIsDrawnDisabledIconIncludedAsync() =>
        ui.RunAsync(() =>
        {
            var icon = new Avalonia.Controls.Shapes.Path { Classes = { "icon" } };
            var delete = new MenuItem { Header = "Delete Project", Classes = { "destructive" }, IsEnabled = false, Icon = icon };
            var view = Opened(new MenuItem { Header = "Rename" }, delete);

            Assert.Equal((Brush(view, "TextDisabledBrush"), Brush(view, "TextDisabledBrush")), (Color(Part<ContentPresenter>(delete, "PART_HeaderPresenter").Foreground), Color(icon.Stroke)));
        }, Cancellation);

    [Fact]
    public Task AMenuItemReachedByTheKeyboardStandsOutFromTheMenuAsync() =>
        ui.RunAsync(() =>
        {
            var rename = new MenuItem { Header = "Rename" };
            var view = Opened(rename, new MenuItem { Header = "Archive" });

            view.Press(Avalonia.Input.Key.Down);
            view.Settle();
            var surface = Color(Presenter(view).Background)!.Value;
            var row = Color(Part<Border>(rename, "PART_LayoutRoot").Background)!.Value;

            Assert.True(Contrast(Over(row, surface), surface) >= 3, $"the selected row is {Contrast(Over(row, surface), surface):0.00}:1 against the menu");
        }, Cancellation);

    [Fact]
    public Task AShortcutKeepsAGapFromAHeaderTrimmedBesideItAsync() =>
        ui.RunAsync(() =>
        {
            var item = new MenuItem { Header = Long, InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.C, Avalonia.Input.KeyModifiers.Control) };
            var view = Opened(item);
            var header = Header(item);
            var gesture = Part<TextBlock>(item, "PART_InputGestureText");
            var end = header.TranslatePoint(new Point(header.Bounds.Width, 0), view.Window)!.Value.X;
            var start = gesture.TranslatePoint(default, view.Window)!.Value.X;

            Assert.True(gesture.Bounds.Width > 0 && start - end >= 12, $"the shortcut starts {start - end:0} after the header ends");
        }, Cancellation);

    [Fact]
    public Task TheKeyboardReachesTheFirstItemOfAMenuThatOpenedUnhighlightedAsync() =>
        ui.RunAsync(() =>
        {
            var rename = new MenuItem { Header = "Rename" };
            var archive = new MenuItem { Header = "Archive" };
            var view = Opened(rename, archive);

            view.Press(Avalonia.Input.Key.Down);

            Assert.Equal((true, false), (rename.IsSelected, archive.IsSelected));
        }, Cancellation);

    [Fact]
    public Task AnItemThePointerReachesIsHighlightedAsync() =>
        ui.RunAsync(() =>
        {
            var rename = new MenuItem { Header = "Rename" };
            var archive = new MenuItem { Header = "Archive" };
            var view = Opened(rename, archive);

            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(view.Window, archive.TranslatePoint(new Point(20, archive.Bounds.Height / 2), view.Window)!.Value);
            view.Settle();

            Assert.Equal((Brush(view, "AccentBrush"), false), (Color(Part<Border>(archive, "PART_LayoutRoot").Background), Color(Part<Border>(rename, "PART_LayoutRoot").Background) == Brush(view, "AccentBrush")));
        }, Cancellation);

    [Fact]
    public Task AMenuJustOpenedDrawsEveryItemOnTheMenusOwnSurfaceAsync() =>
        ui.RunAsync(() =>
        {
            var rename = new MenuItem { Header = "Rename" };
            var archive = new MenuItem { Header = "Archive" };
            _ = Opened(rename, archive);

            Assert.All(new[] { rename, archive }, item => Assert.True(Color(Part<Border>(item, "PART_LayoutRoot").Background) is not { A: > 0 }, $"{item.Header} is drawn on {Color(Part<Border>(item, "PART_LayoutRoot").Background)}"));
        }, Cancellation);

    [Fact]
    public Task ThePointerOnADisabledItemHighlightsNoOtherItemAsync() =>
        ui.RunAsync(() =>
        {
            var rename = new MenuItem { Header = "Rename" };
            var delete = new MenuItem { Header = "Delete Project", Classes = { "destructive" }, IsEnabled = false };
            var view = Opened(rename, new Separator(), delete);

            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(view.Window, delete.TranslatePoint(new Point(20, delete.Bounds.Height / 2), view.Window)!.Value);
            view.Settle();

            Assert.NotEqual(Brush(view, "AccentBrush"), Color(Part<Border>(rename, "PART_LayoutRoot").Background));
        }, Cancellation);

    [Fact]
    public Task EnterOnAMenuJustOpenedInvokesNoItemAsync() =>
        ui.RunAsync(() =>
        {
            var invoked = new List<string>();
            var copy = new MenuItem { Header = "Copy as Markdown" };
            copy.Click += (_, _) => invoked.Add("Copy as Markdown");
            var export = new MenuItem { Header = "Export…" };
            export.Click += (_, _) => invoked.Add("Export…");
            var view = Opened(copy, export);

            view.Press(Avalonia.Input.Key.Enter);

            Assert.Empty(invoked);
        }, Cancellation);

    private static ViewScript Opened(params Control[] items)
    {
        var owner = new Border { Width = 200, Height = 40 };
        var view = ViewScript.Show(new Panel { Children = { owner } }, new object());
        view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
        var menu = new MenuFlyout();

        foreach (var item in items)
        {
            menu.Items.Add(item);
        }

        menu.ShowAt(owner);
        view.Settle();

        return view;
    }

    private static (ViewScript View, ToolTip Tip) Hovered(Control content)
    {
        var owner = new Button { Content = "Hover me" };
        ToolTip.SetTip(owner, content);
        var view = ViewScript.Show(new Panel { Children = { owner } }, new object());
        Theme.Motion.SetIsReduced(view.Window, true);
        view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });

        ToolTip.SetIsOpen(owner, true);
        view.Settle();

        return (view, content.GetVisualAncestors().OfType<ToolTip>().First());
    }

    private static MenuFlyoutPresenter Presenter(ViewScript view) =>
        view.Window.GetVisualDescendants().OfType<MenuFlyoutPresenter>().First();

    private static TextBlock Header(MenuItem item) =>
        Part<ContentPresenter>(item, "PART_HeaderPresenter").GetVisualDescendants().OfType<TextBlock>().First();

    private static T Part<T>(TemplatedControl control, string name)
        where T : Control =>
        control.GetVisualDescendants().OfType<T>().First(part => part.Name == name);

    private static Color? Brush(ViewScript view, string key) =>
        view.Window.TryFindResource(key, ThemeVariant.Dark, out var found) ? Color(found as IBrush) : null;

    private static double Width(ViewScript view, string key) =>
        (double)view.Window.FindResource(key)!;

    private static Color? Color(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

    private static Color Over(Color top, Color bottom)
    {
        var alpha = top.A / 255d;

        return Avalonia.Media.Color.FromRgb(Blend(top.R, bottom.R, alpha), Blend(top.G, bottom.G, alpha), Blend(top.B, bottom.B, alpha));
    }

    private static byte Blend(byte top, byte bottom, double alpha) => (byte)Math.Round((top * alpha) + (bottom * (1 - alpha)));

    private static double Contrast(Color first, Color second)
    {
        var (lighter, darker) = (Math.Max(Luminance(first), Luminance(second)), Math.Min(Luminance(first), Luminance(second)));

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color) => (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));

    private static double Channel(byte value)
    {
        var unit = value / 255d;

        return unit <= 0.03928 ? unit / 12.92 : Math.Pow((unit + 0.055) / 1.055, 2.4);
    }
}
