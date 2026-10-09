using Avala.Components.Canvases;
using Avala.Components.UI.Canvases;
using Avala.Rendering.Highlighting;
using Avala.Rendering.Offer;
using Avala.Rendering.UI.Markdown;
using Avala.Rendering.UI.Source;
using Avala.Rendering.UI.Svg;
using Avala.Sdk;
using Avala.Sdk.UI;

namespace Avala.Rendering.UI;

public sealed class RenderingPlugin : IPlugin, IViewContributor
{
    public PluginInfo Info { get; } = new("avala.rendering", "Canvas renderers");

    public void Register(IPluginRegistrar registrar) => registrar.Services.AddRenderedFormats();

    public void RegisterViews(IViewRegistrar views) =>
        views.AddCanvasRenderer(new MarkdownRenderer())
            .AddCanvasRenderer(new SvgRenderer())
            .AddCanvasSource(CanvasMediaTypes.Mermaid, SourceRenderer.Painting(SourceHighlighter.Mermaid))
            .AddCanvasSource(CanvasMediaTypes.Html, SourceRenderer.Painting(SourceHighlighter.Html));
}
