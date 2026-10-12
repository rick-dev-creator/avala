using Avala.Components.Canvases;
using Avala.Components.UI;
using Avala.Components.UI.Canvases;
using Avala.Components.UI.Theme;
using Avala.Rendering.UI;
using Avala.Sdk.UI;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Svg.Skia;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;
using SvgDrawing = Avalonia.Svg.Skia.Svg;

namespace Avala.Rendering.Tests.Renderers;

public sealed class AnimatedSvgScripts(HeadlessUi ui)
{
    public const string GrowingBar =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\" width=\"100\" height=\"100\">"
        + "<rect x=\"0\" y=\"0\" width=\"100\" height=\"0\" fill=\"#ff0000\"><animate attributeName=\"height\" from=\"0\" to=\"100\" dur=\"2s\" fill=\"freeze\"/></rect></svg>";

    private const string StillBar =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\" width=\"100\" height=\"100\"><rect width=\"100\" height=\"100\" fill=\"#ff0000\"/></svg>";

    private const string Corners =
        "<rect width=\"1000\" height=\"800\" fill=\"#000010\"/>"
        + "<rect x=\"0\" y=\"0\" width=\"20\" height=\"20\" fill=\"#ff0000\"/><rect x=\"980\" y=\"0\" width=\"20\" height=\"20\" fill=\"#ff0000\"/>"
        + "<rect x=\"0\" y=\"780\" width=\"20\" height=\"20\" fill=\"#ff0000\"/><rect x=\"980\" y=\"780\" width=\"20\" height=\"20\" fill=\"#ff0000\"/>";

    private const string AnimatedOrbit =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1000 800\" width=\"400\" height=\"320\">" + Corners
        + "<circle cx=\"500\" cy=\"400\" r=\"40\" fill=\"#ffb020\"><animate attributeName=\"r\" values=\"40;60;40\" dur=\"2s\" repeatCount=\"indefinite\"/></circle></svg>";

    private const string StillOrbit =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1000 800\" width=\"400\" height=\"320\">" + Corners
        + "<circle cx=\"500\" cy=\"400\" r=\"40\" fill=\"#ffb020\"/></svg>";

    private readonly FakeTimeProvider clock = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(GrowingBar, true)]
    [InlineData(StillBar, false)]
    public Task AnAnimatedSvgIsDrawnByTheAnimatingControlAndAStillOneAsAPlainImageAsync(string svg, bool animated) =>
        ui.RunAsync(async () =>
        {
            var view = await ShowAsync(svg);

            Assert.Equal((animated, !animated), (view.Shows("SvgDrawing"), view.Find("Svg") is Image));
        }, Cancellation);

    [Fact]
    public Task AnAnimatedSvgWhoseViewBoxIsLargerThanItsSizeFillsTheCanvasWidthAndDrawsTheWholeViewBoxAsync() =>
        ui.RunAsync(async () =>
        {
            var view = await ShowAsync(AnimatedOrbit, width: 560);
            var drawing = view.Find<SvgDrawing>("SvgDrawing");

            Assert.Equal(new Size(560, 448), Rounded(view.Find("SvgFit").Bounds.Size));
            Assert.Equal(new Size(1000, 800), drawing.Bounds.Size);
            Assert.Equal([true, true, true, true], new[] { (10, 10), (990, 10), (10, 790), (990, 790) }.Select(corner => Red(drawing, corner.Item1, corner.Item2)));
        }, Cancellation);

    [Theory]
    [InlineData(300)]
    [InlineData(560)]
    public Task AnAnimatedAndAStillSvgOfTheSameViewBoxTakeTheSameSizeScaledDownOrUpAsync(double width) =>
        ui.RunAsync(async () =>
        {
            var animated = Rounded((await ShowAsync(AnimatedOrbit, width)).Find("SvgFit").Bounds.Size);
            var still = Rounded((await ShowAsync(StillOrbit, width)).Find("SvgFit").Bounds.Size);

            Assert.Equal((new Size(width, width * 0.8), animated), (still, still));
        }, Cancellation);

    [Fact]
    public Task AnAnimatedSvgShownAgainReusesTheDrawingParsedTheFirstTimeAsync() =>
        ui.RunAsync(async () =>
        {
            var renderers = Renderers();
            var first = await ViewScript.Show(renderers.Build(Rendering(GrowingBar))!, Rendering(GrowingBar)).DrawnAsync();
            var again = await ViewScript.Show(renderers.Build(Rendering(GrowingBar))!, Rendering(GrowingBar)).DrawnAsync();

            Assert.Same(first.Find<SvgDrawing>("SvgDrawing").SvgSource, again.Find<SvgDrawing>("SvgDrawing").SvgSource);
        }, Cancellation);

    [Fact]
    public Task AnAnimationHeldStillDrawsTheFrameOfTheTimeItIsSetToAsync() =>
        ui.RunAsync(async () =>
        {
            var drawing = (await ShowAsync(GrowingBar)).Find<SvgDrawing>("SvgDrawing");
            drawing.AnimationBackend = SvgAnimationHostBackend.Manual;

            drawing.SkSvg!.SetAnimationTime(TimeSpan.Zero);
            var first = (Painted(drawing, y: 25), Painted(drawing, y: 75));
            drawing.SkSvg.SetAnimationTime(TimeSpan.FromSeconds(1));

            Assert.Equal((false, false), first);
            Assert.Equal((true, false), (Painted(drawing, y: 25), Painted(drawing, y: 75)));
        }, Cancellation);

    [Fact]
    public Task AFinishedAnimationHoldsItsLastFrameAndStopsPlayingAsync() =>
        ui.RunAsync(async () =>
        {
            var view = await ShowAsync(GrowingBar);
            var drawing = view.Find<SvgDrawing>("SvgDrawing");
            var playing = (Playing(view), drawing.AnimationBackend);

            clock.Advance(TimeSpan.FromSeconds(2));
            view.Settle();

            Assert.Equal((true, SvgAnimationHostBackend.Default), playing);
            Assert.Equal((false, SvgAnimationHostBackend.Manual, true), (Playing(view), drawing.AnimationBackend, Painted(drawing, y: 95)));
        }, Cancellation);

    [Fact]
    public Task WithReducedMotionTheDrawingShowsItsEndStateWithoutPlayingAsync() =>
        ui.RunAsync(async () =>
        {
            var view = await ShowAsync(GrowingBar);
            Motion.SetIsReduced(view.Window, true);
            var drawing = view.Settle().Find<SvgDrawing>("SvgDrawing");

            Assert.Equal((false, SvgAnimationHostBackend.Manual, true), (Playing(view), drawing.AnimationBackend, Painted(drawing, y: 95)));
        }, Cancellation);

    [Fact]
    public Task WithReducedMotionThePlayButtonOnHoverPlaysTheAnimationOnceAsync() =>
        ui.RunAsync(async () =>
        {
            var view = await ShowAsync(GrowingBar);
            Motion.SetIsReduced(view.Window, true);
            var drawing = view.Settle().Find<SvgDrawing>("SvgDrawing");
            var hidden = view.Shows("PlayAnimation");

            view.Window.MouseMove(Center(view.Find("Svg"), view.Window));
            var hovered = view.Settle().Shows("PlayAnimation");
            view.Click("PlayAnimation");
            var started = (Playing(view), Painted(drawing, y: 25), view.Shows("PlayAnimation"));
            clock.Advance(TimeSpan.FromSeconds(2));
            view.Settle();

            Assert.Equal((false, true), (hidden, hovered));
            Assert.Equal((true, false, false), started);
            Assert.Equal((false, true, true), (Playing(view), Painted(drawing, y: 95), view.Shows("PlayAnimation")));
        }, Cancellation);

    [Fact]
    public Task TheFrameLoopRunsOnlyWhileTheDrawingIsOnScreenAsync() =>
        ui.RunAsync(async () =>
        {
            var (view, scroller, drawing) = await ScrolledAsync();
            var shown = Playing(view);

            view.Window.WindowState = WindowState.Minimized;
            var minimized = Playing(view);
            view.Window.WindowState = WindowState.Normal;
            var restored = Playing(view);
            scroller.Offset = new Vector(0, 2000);
            var scrolledAway = Playing(view.Settle());
            scroller.Offset = new Vector(0, 0);
            var scrolledBack = Playing(view.Settle());
            ((StackPanel)scroller.Content!).Children.Remove(drawing);

            Assert.Equal((true, false, true, false, true, false), (shown, minimized, restored, scrolledAway, scrolledBack, drawing.Classes.Contains(":playing")));
        }, Cancellation);

    [Fact]
    public Task AWheelOverAnAnimatedDrawingScrollsWhatHoldsItAsync() =>
        ui.RunAsync(async () =>
        {
            var (view, scroller, drawing) = await ScrolledAsync();

            view.Window.MouseWheel(Center(drawing, view.Window), new Vector(0, -3));
            view.Settle();

            Assert.True(scroller.Offset.Y > 0);
        }, Cancellation);

    private async Task<(ViewScript View, ScrollViewer Scroller, Control Drawing)> ScrolledAsync()
    {
        var drawing = Renderers().Build(Rendering(GrowingBar))!;
        var scroller = new ScrollViewer
        {
            Content = new StackPanel { Children = { drawing, new Border { Height = 2000 } } },
        };
        CanvasPresenter.SetClock(scroller, clock);

        return (await ViewScript.Show(scroller, Rendering(GrowingBar)).DrawnAsync(), scroller, drawing);
    }

    private Task<ViewScript> ShowAsync(string svg)
    {
        var drawing = Renderers().Build(Rendering(svg))!;
        CanvasPresenter.SetClock(drawing, clock);

        return ViewScript.Show(drawing, Rendering(svg)).DrawnAsync();
    }

    private Task<ViewScript> ShowAsync(string svg, double width)
    {
        var drawing = Renderers().Build(Rendering(svg))!;
        var canvas = new Border { Width = width, MaxHeight = 480, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Child = drawing };
        CanvasPresenter.SetClock(canvas, clock);

        return ViewScript.Show(canvas, Rendering(svg)).DrawnAsync();
    }

    private static Size Rounded(Size size) => new(Math.Round(size.Width), Math.Round(size.Height));

    private static bool Red(SvgDrawing drawing, int x, int y)
    {
        using var bitmap = new SKBitmap(1000, 800);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawPicture(drawing.Picture);

        return bitmap.GetPixel(x, y) == SKColors.Red;
    }

    private static bool Playing(ViewScript view) => view.Find("Svg").Classes.Contains(":playing");

    private static Point Center(Visual visual, Window window) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window)!.Value;

    private static bool Painted(SvgDrawing drawing, int y)
    {
        using var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawPicture(drawing.Picture);

        return bitmap.GetPixel(50, y).Alpha > 0;
    }

    private static CanvasRendering Rendering(string svg) => new("image/svg+xml", svg, true, 1);

    private static ViewRegistry Renderers()
    {
        var views = new ViewRegistry();
        new RenderingPlugin().RegisterViews(views);

        return views;
    }
}
