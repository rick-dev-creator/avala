using Avala.Components.Canvases;
using Avala.Rendering.UI;
using Avala.Sdk.UI;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using MarkView.Avalonia;

[assembly: AssemblyFixture(typeof(HeadlessUi))]

namespace Avala.Rendering.Tests.Renderers;

public sealed class CanvasRendererScripts(HeadlessUi ui)
{
    private const string Drawing =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 120 40\"><rect x=\"4\" y=\"4\" width=\"112\" height=\"32\" rx=\"6\" fill=\"none\" stroke=\"#8DA2FB\"/></svg>";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("text/markdown", true)]
    [InlineData("text/markdown; charset=utf-8", true)]
    [InlineData("image/svg+xml", true)]
    [InlineData("text/vnd.mermaid", true)]
    [InlineData("text/html", true)]
    [InlineData("application/pdf", false)]
    public void ThePluginRegistersARendererForEveryKindOfCanvasTheCoreProduces(string mediaType, bool rendered)
    {
        Assert.Equal(rendered, Renderers().Match(new CanvasRendering(mediaType, "x", true, 1)));
    }

    [Fact]
    public Task MarkdownIsRenderedAsTextWithoutItsMarksAsync() =>
        ui.RunAsync(() =>
        {
            var view = Show("text/markdown", "# Fix JPY rounding\n\nRound **once**, in minor units.\n\n- `ToMinor`\n- `FromMinor`");

            Assert.Contains("Fix JPY rounding", Texts(view.Window));
            Assert.DoesNotContain(Texts(view.Window), text => text.Contains('#', StringComparison.Ordinal) || text.Contains("**", StringComparison.Ordinal));
        }, Cancellation);

    [Fact]
    public Task MarkdownNeverLoadsAnImageFromOutsideAsync() =>
        ui.RunAsync(() =>
        {
            var view = Show("text/markdown", "Before ![usage chart](https://example.com/chart.png) after");

            Assert.Empty(view.All<Image>());
            Assert.Contains(Texts(view.Window), text => text.Contains("[usage chart]", StringComparison.Ordinal));
        }, Cancellation);

    [Theory]
    [InlineData("Inline <img src=\"https://example.com/a.png\"> image")]
    [InlineData("<div>\n<img src=\"https://example.com/a.png\">\n</div>")]
    [InlineData("<script>fetch('https://example.com')</script>")]
    public Task RawHtmlInMarkdownIsNeverRunOrFetchedAsync(string markdown) =>
        ui.RunAsync(() =>
        {
            var view = Show("text/markdown", markdown);

            Assert.Empty(view.All<Image>());
            Assert.True(view.Shows("Markdown"));
        }, Cancellation);

    [Fact]
    public Task AMarkdownLinkDoesNotLeaveTheHarnessAsync() =>
        ui.RunAsync(() =>
        {
            var view = Show("text/markdown", "[docs](https://example.com)");
            var viewer = view.Window.GetLogicalDescendants().OfType<MarkdownViewer>().Single();
            var clicked = new MarkView.Avalonia.Rendering.LinkClickedEventArgs("https://example.com") { RoutedEvent = MarkdownViewer.LinkClickedEvent };

            viewer.RaiseEvent(clicked);

            Assert.True(clicked.Handled);
        }, Cancellation);

    [Fact]
    public Task AnSvgDrawingIsRenderedAsAnImageAsync() =>
        ui.RunAsync(() =>
        {
            var view = Show("image/svg+xml", Drawing);

            Assert.True(view.Shows("Svg"));
            Assert.False(view.Shows("Blocked"));
        }, Cancellation);

    [Fact]
    public Task AnSvgDrawingSaysWhatWasBlockedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Show("image/svg+xml", Drawing.Replace("</svg>", "<script>alert(1)</script><image href=\"https://example.com/t.png\"/></svg>", StringComparison.Ordinal));

            Assert.True(view.Shows("Svg"));
            Assert.Equal("2 scripts or external references were blocked.", view.TextOf("Blocked"));
        }, Cancellation);

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"></svg")]
    public void AnIncompleteSvgRendersNothingSoThePreviousDrawingStays(string partial) =>
        Assert.Null(Renderers().Build(new CanvasRendering("image/svg+xml", partial, false, 1)));

    [Theory]
    [InlineData("text/vnd.mermaid", "flowchart LR\n  A --> B", RenderingPlugin.MermaidExplanation, "flowchart")]
    [InlineData("text/html", "<p class=\"x\">Total</p>", RenderingPlugin.HtmlExplanation, "<p")]
    public Task MermaidAndHtmlShowTheirHighlightedSourceAndWhyAsync(string mediaType, string source, string explanation, string highlighted) =>
        ui.RunAsync(() =>
        {
            var view = Show(mediaType, source);
            var runs = view.Find<SelectableTextBlock>("CanvasSource").Inlines!.OfType<Run>().ToList();

            Assert.Equal(explanation, view.TextOf("CanvasNote"));
            Assert.Equal(source, string.Concat(runs.Select(run => run.Text)));
            Assert.Contains(runs, run => run.Text == highlighted && run.Foreground is not null);
        }, Cancellation);

    private static ViewRegistry Renderers()
    {
        var views = new ViewRegistry();
        new RenderingPlugin().RegisterViews(views);

        return views;
    }

    private static ViewScript Show(string mediaType, string content)
    {
        var rendering = new CanvasRendering(mediaType, content, true, 1);

        return ViewScript.Show(Renderers().Build(rendering) ?? new TextBlock { Text = "nothing" }, rendering);
    }

    private static List<string> Texts(Window window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(run => run.Text)) : text.Text ?? string.Empty)];
}
