using Avala.Components.Canvases;
using Avala.Components.UI.Canvases;
using Avala.Sdk;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Components.UI.Tests;

public sealed class CanvasSurfaceViewScripts(HeadlessUi ui)
{
    private const string Sketch = "text/x-sketch";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ACanvasShowsItsTitleItsKindAndItsDrawingAsync() =>
        ui.RunAsync(() =>
        {
            var view = Show(Surface(Draft("circle", CanvasPhase.Completed)));

            Assert.Equal(("Request flow", "text/x-sketch", false), (view.TextOf("Title"), view.TextOf("MediaLabel"), view.Shows("Status")));
            Assert.Equal(["drawn: circle"], Drawings(view));
        }, Cancellation);

    [Fact]
    public Task WhileStreamingAnIncompleteSnapshotKeepsThePreviousDrawingAsync() =>
        ui.RunAsync(() =>
        {
            var surface = Surface(Draft("circle", CanvasPhase.Streaming));
            var view = Show(surface);

            surface.Show(Draft("circle, squ", CanvasPhase.Streaming, incomplete: true));
            view.Settle();

            Assert.Equal(["drawn: circle"], Drawings(view));
            Assert.False(view.Shows("CanvasSource"));
            Assert.True(view.Shows("Streaming"));
        }, Cancellation);

    [Fact]
    public Task AStreamingDrawingArrivesOverThePreviousOneAndReplacesItOnceOpaqueAsync() =>
        ui.RunAsync(() =>
        {
            var surface = Surface(Draft("circle", CanvasPhase.Streaming));
            var view = Show(surface);
            var presenter = view.Find<CanvasPresenter>("Drawing");

            surface.Show(Draft("circle, square", CanvasPhase.Streaming));
            view.Settle();
            var arriving = presenter.IsArriving;
            var layers = Drawings(view);
            Elapse(view);

            Assert.True(arriving);
            Assert.Equal(["drawn: circle", "drawn: circle, square"], layers);
            Assert.Equal((false, 1d), (presenter.IsArriving, presenter.Children[0].Opacity));
            Assert.Equal(["drawn: circle, square"], Drawings(view));
        }, Cancellation);

    [Fact]
    public Task WithReducedMotionANewDrawingReplacesThePreviousOneWithoutFadingAsync() =>
        ui.RunAsync(() =>
        {
            var surface = Surface(Draft("circle", CanvasPhase.Streaming));
            var view = Show(surface);
            Theme.Motion.SetIsReduced(view.Window, true);
            var presenter = view.Find<CanvasPresenter>("Drawing");

            surface.Show(Draft("circle, square", CanvasPhase.Streaming));
            view.Settle();

            Assert.Equal((false, 1d), (presenter.IsArriving, presenter.Children[0].Opacity));
            Assert.Equal(["drawn: circle, square"], Drawings(view));
        }, Cancellation);

    [Fact]
    public Task ACanvasThatWasNotOfferedShowsItsSourceWithANoteEvenWhenARendererKnowsItsKindAsync() =>
        ui.RunAsync(() =>
        {
            var view = Show(Surface(Draft("circle", CanvasPhase.Completed) with { IsOffered = false }));

            Assert.Equal("Avala does not offer text/x-sketch canvases to agents, so this one is not drawn. Showing its source.", view.TextOf("CanvasNote"));
            Assert.Equal("circle", Source(view));
            Assert.Empty(Drawings(view));
        }, Cancellation);

    [Fact]
    public Task AFinishedCanvasThatCannotBeRenderedShowsItsSourceWithANoteAsync() =>
        ui.RunAsync(() =>
        {
            var surface = Surface(Draft("circle", CanvasPhase.Streaming));
            var view = Show(surface);

            surface.Show(Draft("circle, squ", CanvasPhase.Failed, incomplete: true));
            Elapse(view);

            Assert.Equal("This text/x-sketch could not be rendered. Showing its source.", view.TextOf("CanvasNote"));
            Assert.Equal("circle, squ!", Source(view));
            Assert.Equal(("Failed", false), (view.TextOf("Status"), view.Shows("Streaming")));
        }, Cancellation);

    [Fact]
    public Task ACanvasOfAKindNoRendererKnowsShowsItsSourceAsync() =>
        ui.RunAsync(() =>
        {
            var view = Show(Surface(new CanvasDraft("Data", "application/x-unknown", "a,b\n1,2", CanvasPhase.Completed)));

            Assert.Equal("Avala has no renderer for application/x-unknown. Showing its source.", view.TextOf("CanvasNote"));
            Assert.Equal("a,b\n1,2", Source(view));
        }, Cancellation);

    [Fact]
    public Task AHugeCanvasShowsTheStartOfItsSourceInsteadOfRenderingItAsync() =>
        ui.RunAsync(() =>
        {
            var huge = new string('x', CanvasRendering.RenderableLength + 1);
            var view = Show(Surface(Draft(huge, CanvasPhase.Completed)));

            Assert.Equal("This canvas is too large to render (512 KB). Showing the start of its source.", view.TextOf("CanvasNote"));
            Assert.Equal(CanvasRendering.SourcePreviewLength, Source(view).Length);
            Assert.Equal("458,753 more characters not shown", view.TextOf("CanvasTruncated"));
            Assert.Empty(Drawings(view));
        }, Cancellation);

    [Fact]
    public Task SteppingBackRedrawsTheEarlierVersionAndTheLatestReturnsAsync() =>
        ui.RunAsync(() =>
        {
            var surface = Surface(Draft("circle", CanvasPhase.Streaming), Draft("circle, square", CanvasPhase.Completed));
            var view = Show(surface);

            view.Click("PreviousVersion");
            Elapse(view);
            var earlier = (view.TextOf("VersionText"), Drawings(view));
            view.Click("VersionLabel");
            Elapse(view);

            Assert.Equal(("1 of 2", "drawn: circle"), (earlier.Item1, Assert.Single(earlier.Item2)));
            Assert.Equal(("2 of 2", "drawn: circle, square"), (view.TextOf("VersionText"), Assert.Single(Drawings(view))));
        }, Cancellation);

    [Fact]
    public Task ASingleVersionShowsNoStepperAsync() =>
        ui.RunAsync(() =>
        {
            var view = Show(Surface(Draft("circle", CanvasPhase.Completed)));

            Assert.False(view.Shows("Versions"));
        }, Cancellation);

    [Fact]
    public Task SwitchingToAnotherCanvasMidStreamShowsOnlyThatCanvasAsync() =>
        ui.RunAsync(() =>
        {
            var first = Surface(Draft("circle", CanvasPhase.Streaming));
            var second = Surface(new CanvasDraft("Other", Sketch, "line", CanvasPhase.Streaming));
            var view = Show(first);

            view.Window.Content = second;
            view.Settle();
            second.Show(new CanvasDraft("Other", Sketch, "line, arc!", CanvasPhase.Streaming));
            first.Show(Draft("circle, square", CanvasPhase.Streaming));
            Elapse(view);

            Assert.Equal("Other", view.TextOf("Title"));
            Assert.Equal(["drawn: line"], Drawings(view));
        }, Cancellation);

    [Fact]
    public Task OpeningShowsTheDrawingLargerUntilEscapeClosesItAsync() =>
        ui.RunAsync(() =>
        {
            var surface = Surface(Draft("circle", CanvasPhase.Completed));
            var view = Show(surface);

            view.Click("Open");
            var popup = view.Find<Popup>("Focused");
            var card = view.Find<Border>("FocusedCard");
            var opened = (popup.IsOpen, view.TextOf("FocusedTitle"), card.Width);
            var drawn = Drawings(card);
            TopLevel.GetTopLevel(card)!.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            view.Settle();

            Assert.Equal((true, "Request flow", 640 * 0.86), opened);
            Assert.Equal(["drawn: circle"], drawn);
            Assert.Equal((false, false), (popup.IsOpen, surface.IsOpen));
        }, Cancellation);

    [Fact]
    public Task TheDesignTimeSurfaceRendersAsync() =>
        ui.RunAsync(() =>
        {
            var view = Show(new DesignCanvasSurfaceViewModel());

            Assert.Equal(("Rounding path", "SVG", "3 of 3"), (view.TextOf("Title"), view.TextOf("MediaLabel"), view.TextOf("VersionText")));
            Assert.Equal(DesignCanvasSurfaceViewModel.RoundingPath, Source(view));
        }, Cancellation);

    private static ViewScript Show(object surface)
    {
        SketchRenderer.Register();
        var view = ViewScript.Show(surface);
        CanvasPresenter.SetClock(view.Window, new FakeTimeProvider());

        return view;
    }

    private static CanvasSurfaceViewModel Surface(params CanvasDraft[] drafts)
    {
        var surface = new CanvasSurfaceViewModel();

        foreach (var draft in drafts)
        {
            surface.Show(draft);
        }

        return surface;
    }

    private static CanvasDraft Draft(string content, CanvasPhase phase, bool incomplete = false) =>
        new("Request flow", Sketch, incomplete ? content + "!" : content, phase);

    private static IReadOnlyList<string> Drawings(Visual root) =>
        [.. root.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Name == "Sketch").Select(text => text.Text ?? string.Empty)];

    private static IReadOnlyList<string> Drawings(ViewScript view) => Drawings(view.Window);

    private static string Source(ViewScript view) =>
        string.Concat(view.Find<SelectableTextBlock>("CanvasSource").Inlines!.OfType<Run>().Select(run => run.Text));

    private static void Elapse(ViewScript view)
    {
        ((FakeTimeProvider)CanvasPresenter.GetClock(view.Window)).Advance(TimeSpan.FromSeconds(1));
        view.Settle();
    }

    private sealed class SketchRenderer : ICanvasRenderer
    {
        private static bool registered;

        public static void Register()
        {
            if (!registered)
            {
                HeadlessApp.Views.AddCanvasRenderer(new SketchRenderer());
                registered = true;
            }
        }

        public string MediaType => Sketch;

        public Option<Control> Render(string content) =>
            content.EndsWith('!') ? Option<Control>.None : new TextBlock { Name = "Sketch", Text = $"drawn: {content}" };
    }
}
