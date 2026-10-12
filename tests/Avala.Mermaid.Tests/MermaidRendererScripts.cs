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
using Microsoft.Extensions.Time.Testing;

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
        await using var composition = PluginComposition.Of(new MermaidPlugin(new FakeTimeProvider()), new AvalaPaths("unused"), _ => { });
        var views = new RecordingRegistrar();
        new MermaidPlugin(new FakeTimeProvider()).RegisterViews(views);

        var offered = Assert.Single(composition.All<CanvasFormat>());

        Assert.Equal(("text/vnd.mermaid", "Mermaid"), (offered.MediaType, offered.Name));
        Assert.Equal(["text/vnd.mermaid"], views.Templates.OfType<CanvasRendererTemplate>().Select(template => template.MediaType));
    }

    [Fact]
    public Task AnOfferedMermaidCanvasIsDrawnThroughTheSvgRendererAsync() =>
        ui.RunAsync(async () =>
        {
            var view = await ShownAsync(Flow, ThemeVariant.Dark);

            Assert.True(view.Shows("Mermaid"));
            Assert.True(view.Shows("Svg"));
            Assert.False(view.Shows("Blocked"));
        }, Cancellation);

    [Fact]
    public Task TheDiagramIsDrawnInTheColorsOfTheWindowsThemeAsync() =>
        ui.RunAsync(async () =>
        {
            var dark = Drawing(await ShownAsync(Flow, ThemeVariant.Dark));
            var light = Drawing(await ShownAsync(Flow, ThemeVariant.Light));

            Assert.Contains("#EDEDEF", dark, StringComparison.Ordinal);
            Assert.Contains("#1D1D1F", light, StringComparison.Ordinal);
            Assert.DoesNotContain("#EDEDEF", light, StringComparison.Ordinal);
        }, Cancellation);

    [Fact]
    public Task SwitchingTheThemeRedrawsTheDiagramWhileTheEarlierDrawingStaysUntilTheNewOneIsReadyAsync() =>
        ui.RunAsync(async () =>
        {
            var view = await ShownAsync(Flow + "  Checking --> Switched\n", ThemeVariant.Dark);

            view.Window.RequestedThemeVariant = ThemeVariant.Light;
            var meanwhile = Drawing(view);
            await view.DrawnAsync();

            Assert.Contains("#EDEDEF", meanwhile, StringComparison.Ordinal);
            Assert.Contains("#1D1D1F", Drawing(view), StringComparison.Ordinal);
        }, Cancellation);

    [Fact]
    public Task ADiagramShownAgainIsDrawnOnceAndReusedAsync() =>
        ui.RunAsync(async () =>
        {
            var source = Flow + "  Checking --> Reused\n";
            var drawn = Drawing(await ShownAsync(source, ThemeVariant.Dark));

            var again = Drawing(Show(source, ThemeVariant.Dark));

            Assert.Same(drawn, again);
        }, Cancellation);

    [Fact]
    public Task MermaidThatCannotBeParsedShowsItsSourceAndIsNotDrawnAgainAsync() =>
        ui.RunAsync(async () =>
        {
            var view = await ShownAsync("not a diagram at all", ThemeVariant.Dark);

            Assert.True(view.Shows("CanvasSource"));
            Assert.Null(Renderers().Build(new CanvasRendering(CanvasMediaTypes.Mermaid, "not a diagram at all", true, 1)));
        }, Cancellation);

    private static Task<ViewScript> ShownAsync(string source, ThemeVariant variant) => Show(source, variant).DrawnAsync();

    private static ViewScript Show(string source, ThemeVariant variant)
    {
        RegisterRenderers();
        var rendering = new CanvasRendering(CanvasMediaTypes.Mermaid, source, true, 1);
        var window = new Window { Width = 640, Height = 480, RequestedThemeVariant = variant, Content = rendering };

        return ViewScript.Present(window, rendering);
    }

    private static string Drawing(ViewScript view) =>
        Assert.IsType<CanvasRendering>(view.Find<ContentControl>("Mermaid").Content).Content;

    private static ViewRegistry Renderers() => HeadlessApp.Views;

    private static void RegisterRenderers()
    {
        if (!registered)
        {
            new RenderingPlugin().RegisterViews(HeadlessApp.Views);
            new MermaidPlugin(new FakeTimeProvider()).RegisterViews(HeadlessApp.Views);
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
