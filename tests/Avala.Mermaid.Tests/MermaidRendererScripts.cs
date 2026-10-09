using Avala.Canvas.Contracts;
using Avala.Components.Canvases;
using Avala.Components.UI.Canvases;
using Avala.Mermaid.UI;
using Avala.Rendering.UI;
using Avala.Sdk;
using Avala.Sdk.UI;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using Avalonia.VisualTree;

[assembly: AssemblyFixture(typeof(HeadlessUi))]

namespace Avala.Mermaid.Tests;

public sealed class MermaidRendererScripts(HeadlessUi ui)
{
    private const string Flow = "flowchart LR\n  Submitted --> Running\n  Running --> Checking\n";

    private static bool registered;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ThePluginOffersMermaidAndRegistersItsRendererAsync()
    {
        await using var composition = PluginComposition.Of(new MermaidPlugin(), new AvalaPaths("unused"), _ => { });
        var views = new RecordingRegistrar();
        new MermaidPlugin().RegisterViews(views);

        var offered = Assert.Single(composition.All<CanvasFormat>());

        Assert.Equal(("text/vnd.mermaid", "Mermaid"), (offered.MediaType, offered.Name));
        Assert.Equal(["text/vnd.mermaid"], views.Templates.OfType<CanvasRendererTemplate>().Select(template => template.MediaType));
    }

    [Fact]
    public Task AnOfferedMermaidCanvasIsDrawnThroughTheSvgRendererAsync() =>
        ui.RunAsync(() =>
        {
            var view = Show(Flow, ThemeVariant.Dark);

            Assert.True(view.Shows("Mermaid"));
            Assert.True(view.Shows("Svg"));
            Assert.False(view.Shows("Blocked"));
        }, Cancellation);

    [Fact]
    public Task TheDiagramIsDrawnInTheColorsOfTheWindowsThemeAsync() =>
        ui.RunAsync(() =>
        {
            var dark = Drawing(Show(Flow, ThemeVariant.Dark));
            var light = Drawing(Show(Flow, ThemeVariant.Light));

            Assert.Contains("#EDEDEF", dark, StringComparison.Ordinal);
            Assert.Contains("#1D1D1F", light, StringComparison.Ordinal);
            Assert.DoesNotContain("#EDEDEF", light, StringComparison.Ordinal);
        }, Cancellation);

    [Fact]
    public Task SwitchingTheThemeRedrawsTheDiagramAsync() =>
        ui.RunAsync(() =>
        {
            var view = Show(Flow, ThemeVariant.Dark);

            view.Window.RequestedThemeVariant = ThemeVariant.Light;
            view.Settle();

            Assert.Contains("#1D1D1F", Drawing(view), StringComparison.Ordinal);
        }, Cancellation);

    [Fact]
    public void MermaidThatCannotBeParsedIsNotDrawn() =>
        Assert.Null(Renderers().Build(new CanvasRendering(CanvasMediaTypes.Mermaid, "not a diagram at all", true, 1)));

    private static ViewScript Show(string source, ThemeVariant variant)
    {
        RegisterRenderers();
        var rendering = new CanvasRendering(CanvasMediaTypes.Mermaid, source, true, 1);
        var window = new Window { Width = 640, Height = 480, RequestedThemeVariant = variant, Content = rendering };

        return ViewScript.Present(window, rendering);
    }

    private static string Drawing(ViewScript view) =>
        Assert.IsType<CanvasRendering>(view.Find<ContentControl>("Mermaid").Content).Content;

    private static ViewRegistry Renderers()
    {
        var views = new ViewRegistry();
        new MermaidPlugin().RegisterViews(views);

        return views;
    }

    private static void RegisterRenderers()
    {
        if (!registered)
        {
            new RenderingPlugin().RegisterViews(HeadlessApp.Views);
            new MermaidPlugin().RegisterViews(HeadlessApp.Views);
            registered = true;
        }
    }

    private sealed class RecordingRegistrar : IViewRegistrar
    {
        public List<IDataTemplate> Templates { get; } = [];

        public void Register<TViewModel, TView>()
            where TViewModel : class
            where TView : Control, new()
        {
        }

        public void Register(IDataTemplate template) => Templates.Add(template);
    }
}
