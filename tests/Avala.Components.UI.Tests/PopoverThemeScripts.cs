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
    public Task AHighlightedMenuItemSitsOnTheQuietHoverFillInsteadOfTheAccentAsync() =>
        ui.RunAsync(() =>
        {
            var rename = new MenuItem { Header = "Rename" };
            var view = Opened(rename, new MenuItem { Header = "Archive" });

            rename.IsSelected = true;
            view.Settle();

            Assert.Equal((Brush(view, "FillHoverBrush"), Brush(view, "TextPrimaryBrush")), (Color(Part<Border>(rename, "PART_LayoutRoot").Background), Color(Part<ContentPresenter>(rename, "PART_HeaderPresenter").Foreground)));
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

    private static ViewScript Opened(params MenuItem[] items)
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
}
