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

    private readonly FakeTimeProvider clock = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(GrowingBar, true)]
    [InlineData(StillBar, false)]
    public Task AnAnimatedSvgIsDrawnByTheAnimatingControlAndAStillOneAsAPlainImageAsync(string svg, bool animated) =>
        ui.RunAsync(() =>
        {
            var view = Show(svg);

            Assert.Equal((animated, !animated), (view.Shows("SvgDrawing"), view.Find("Svg") is Image));
        }, Cancellation);

    [Fact]
    public Task AnAnimationHeldStillDrawsTheFrameOfTheTimeItIsSetToAsync() =>
        ui.RunAsync(() =>
        {
            var drawing = Show(GrowingBar).Find<SvgDrawing>("SvgDrawing");
            drawing.AnimationBackend = SvgAnimationHostBackend.Manual;

            drawing.SkSvg!.SetAnimationTime(TimeSpan.Zero);
            var first = (Painted(drawing, y: 25), Painted(drawing, y: 75));
            drawing.SkSvg.SetAnimationTime(TimeSpan.FromSeconds(1));

            Assert.Equal((false, false), first);
            Assert.Equal((true, false), (Painted(drawing, y: 25), Painted(drawing, y: 75)));
        }, Cancellation);

    [Fact]
    public Task AFinishedAnimationHoldsItsLastFrameAndStopsPlayingAsync() =>
        ui.RunAsync(() =>
        {
            var view = Show(GrowingBar);
            var drawing = view.Find<SvgDrawing>("SvgDrawing");
            var playing = (Playing(view), drawing.AnimationBackend);

            clock.Advance(TimeSpan.FromSeconds(2));
            view.Settle();

            Assert.Equal((true, SvgAnimationHostBackend.Default), playing);
            Assert.Equal((false, SvgAnimationHostBackend.Manual, true), (Playing(view), drawing.AnimationBackend, Painted(drawing, y: 95)));
        }, Cancellation);

    [Fact]
    public Task WithReducedMotionTheDrawingShowsItsEndStateWithoutPlayingAsync() =>
        ui.RunAsync(() =>
        {
            var view = Show(GrowingBar);
            Motion.SetIsReduced(view.Window, true);
            var drawing = view.Settle().Find<SvgDrawing>("SvgDrawing");

            Assert.Equal((false, SvgAnimationHostBackend.Manual, true), (Playing(view), drawing.AnimationBackend, Painted(drawing, y: 95)));
        }, Cancellation);

    [Fact]
    public Task WithReducedMotionThePlayButtonOnHoverPlaysTheAnimationOnceAsync() =>
        ui.RunAsync(() =>
        {
            var view = Show(GrowingBar);
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
        ui.RunAsync(() =>
        {
            var (view, scroller, drawing) = Scrolled();
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
        ui.RunAsync(() =>
        {
            var (view, scroller, drawing) = Scrolled();

            view.Window.MouseWheel(Center(drawing, view.Window), new Vector(0, -3));
            view.Settle();

            Assert.True(scroller.Offset.Y > 0);
        }, Cancellation);

    private (ViewScript View, ScrollViewer Scroller, Control Drawing) Scrolled()
    {
        var drawing = Renderers().Build(Rendering(GrowingBar))!;
        var scroller = new ScrollViewer
        {
            Content = new StackPanel { Children = { drawing, new Border { Height = 2000 } } },
        };
        CanvasPresenter.SetClock(scroller, clock);

        return (ViewScript.Show(scroller, Rendering(GrowingBar)), scroller, drawing);
    }

    private ViewScript Show(string svg)
    {
        var drawing = Renderers().Build(Rendering(svg))!;
        CanvasPresenter.SetClock(drawing, clock);

        return ViewScript.Show(drawing, Rendering(svg));
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
