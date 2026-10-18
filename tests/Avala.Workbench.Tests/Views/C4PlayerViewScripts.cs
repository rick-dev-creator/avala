using Avala.Components.UI.Graphs;
using Avala.Components.UI.Zooming;
using Avala.Components.Zooming;
using Avala.Testing;
using Avala.Testing.UI;
using Avala.Workbench.Docs;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class C4PlayerViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ThePlayerDrawsEveryElementWithItsFrameAndRelationsAndListsTheSectionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignC4PlayerViewModel(), ThemeVariant.Dark, 1240, 720);

            var graph = view.All<C4GraphPanel>().Single();
            Assert.Equal(6, view.All<TextBlock>().Count(text => text.Name == "NodeLabel"));
            Assert.Equal(["ledger"], graph.Frames.Select(frame => frame.Key));
            Assert.Equal(6, graph.Links.Count);
            Assert.Equal(["Webhooks", "Payments"], view.All<TextBlock>().Where(text => text.Name == "SectionTitle").Select(text => text.Text));
            Assert.Equal("Step 2 of 2 · Payments", view.TextOf("Position"));
        }, Cancellation);

    [Fact]
    public Task ClickingASectionMovesTheDiagramToItsStepAndBackReturnsOneStepAsync() =>
        ui.RunAsync(() =>
        {
            using var player = Player();
            var view = Screen.Show(player, ThemeVariant.Dark, 1240, 720);

            view.Click(view.All<Button>().First(button => button.Name == "Section"));
            var chosen = view.TextOf("Position");
            view.Click("Back");

            Assert.Equal("Step 1 of 2 · Webhooks", chosen);
            Assert.Equal("2 steps", view.TextOf("Position"));
        }, Cancellation);

    [Fact]
    public Task AnOpenedDiagramIsFittedWholeAndCentredWithEveryLabelInsideItAsync() =>
        ui.RunAsync(() =>
        {
            using var player = Player();
            var view = Screen.Show(player, ThemeVariant.Dark, 1000, 520);
            var surface = view.Find<ZoomSurface>("Surface");
            var graph = view.All<C4GraphPanel>().Single();

            var room = new Rect(surface.Bounds.Size).Deflate(surface.FitMargin);
            var shown = surface.Shown;

            Assert.InRange(player.Zoom.Scale, 0.3, 0.99);
            Assert.Equal((true, player.Zoom.Scale), (player.Zoom.IsFitted, surface.Scale));
            Assert.True(room.Inflate(0.5).Contains(shown), $"{shown} is not inside {room}");
            Assert.True(Math.Abs((shown.X - room.X) - (room.Right - shown.Right)) < 1 || Math.Abs((shown.Y - room.Y) - (room.Bottom - shown.Bottom)) < 1);
            Assert.All(graph.Labels, label => Assert.True(new Rect(graph.Bounds.Size).Contains(label), $"{label} is outside {graph.Bounds.Size}"));
        }, Cancellation);

    [Fact]
    public Task TheWheelZoomsInAtThePointerKeepingWhatIsUnderItInPlaceAsync() =>
        ui.RunAsync(() =>
        {
            using var player = Player();
            var view = Screen.Show(player, ThemeVariant.Dark, 1240, 720);
            var surface = view.Find<ZoomSurface>("Surface");
            var fitted = surface.Shown;
            var pointer = new Point(fitted.X + (fitted.Width / 3), fitted.Y + (fitted.Height / 4));
            var before = (pointer - fitted.TopLeft) / player.Zoom.Scale;

            view.Window.MouseWheel(surface.TranslatePoint(pointer, view.Window)!.Value, new Vector(0, 1));
            view.Settle();
            var after = (pointer - surface.Shown.TopLeft) / player.Zoom.Scale;

            Assert.Equal(fitted.Width * ZoomSurface.WheelStep, surface.Shown.Width, 3);
            Assert.Equal((before.X, before.Y), (after.X, after.Y), Close);
            Assert.Equal((false, true), (player.Zoom.IsFitted, view.Find<Button>("Fit").IsEffectivelyEnabled));
        }, Cancellation);

    [Fact]
    public Task InTheDocsPaneThePlainWheelScrollsThePaneAndTheCommandWheelZoomsAsync() =>
        ui.RunAsync(() =>
        {
            using var player = Player();
            player.IsCompact = true;
            var view = Screen.ShowScrolled(player, 520, 480);
            var surface = view.Find<ZoomSurface>("Surface");
            var pane = view.Find<ScrollViewer>("Pane");
            var center = surface.TranslatePoint(new Point(surface.Bounds.Width / 2, surface.Bounds.Height / 2), view.Window)!.Value;
            var fitted = player.Zoom.Scale;

            view.Window.MouseWheel(center, new Vector(0, -1));
            view.Settle();
            var scrolled = (pane.Offset.Y, player.Zoom.Scale);
            pane.Offset = default;
            view.Settle();
            center = surface.TranslatePoint(new Point(surface.Bounds.Width / 2, surface.Bounds.Height / 2), view.Window)!.Value;
            view.Window.MouseWheel(center, new Vector(0, 1), RawInputModifiers.Control);
            view.Settle();

            Assert.True(scrolled.Y > 0, "the plain wheel did not scroll the pane");
            Assert.Equal(fitted, scrolled.Item2);
            Assert.Equal(fitted * ZoomSurface.WheelStep, player.Zoom.Scale, 3);
        }, Cancellation);

    [Fact]
    public Task DraggingTheDiagramPansItAndOffersTheFitAgainAsync() =>
        ui.RunAsync(() =>
        {
            using var player = Player();
            var view = Screen.Show(player, ThemeVariant.Dark, 1240, 720);
            var surface = view.Find<ZoomSurface>("Surface");
            var fitted = surface.Shown;

            view.Drag("Surface", 80);

            Assert.Equal((fitted.X + 80, fitted.Y), (surface.Shown.X, surface.Shown.Y), Close);
            Assert.Equal((false, fitted.Width), (player.Zoom.IsFitted, surface.Shown.Width));
            Assert.True(view.Find<Button>("Fit").IsEffectivelyEnabled);
        }, Cancellation);

    [Fact]
    public Task TheZoomButtonsZoomAboutTheCentreAndFitBringsTheWholeDiagramBackAsync() =>
        ui.RunAsync(() =>
        {
            using var player = Player();
            var view = Screen.Show(player, ThemeVariant.Dark, 1240, 720);
            var surface = view.Find<ZoomSurface>("Surface");
            var fitted = surface.Shown;
            var middle = new Point(surface.Bounds.Width / 2, surface.Bounds.Height / 2);
            var before = (middle - fitted.TopLeft) / fitted.Width;

            view.Click("ZoomIn");
            var zoomed = surface.Shown;
            var after = (middle - zoomed.TopLeft) / zoomed.Width;
            view.Click("Fit");

            Assert.Equal(fitted.Width * ZoomViewModel.Step, zoomed.Width, 3);
            Assert.Equal((before.X, before.Y), (after.X, after.Y), Close);
            Assert.Equal(fitted, surface.Shown);
            Assert.False(view.Find<Button>("Fit").IsEffectivelyEnabled);
        }, Cancellation);

    [Fact]
    public Task APlayerZoomedBeforeItsViewIsShownOpensCentredAtItsZoomAsync() =>
        ui.RunAsync(() =>
        {
            using var player = Player();
            player.Zoom.Scale = 0.9;
            player.Zoom.ZoomInCommand.Execute(null);

            var view = Screen.Show(player, ThemeVariant.Dark, 1240, 720);
            var surface = view.Find<ZoomSurface>("Surface");
            var shown = surface.Shown;

            Assert.Equal((1.125, 1.125), (surface.Scale, player.Zoom.Scale), Close);
            Assert.Equal((shown.X, shown.Y), (surface.Bounds.Width - shown.Right, surface.Bounds.Height - shown.Bottom), Close);
        }, Cancellation);

    private static readonly IEqualityComparer<(double, double)> Close = EqualityComparer<(double, double)>.Create((one, other) => Math.Abs(one.Item1 - other.Item1) < 0.01 && Math.Abs(one.Item2 - other.Item2) < 0.01, pair => 0);

    private static C4PlayerViewModel Player()
    {
        var player = new C4PlayerViewModel(TimeProvider.System, new InlineUiDispatcher(), new ZoomViewModel());
        player.Show(DesignStories.Ledger());

        return player;
    }
}
