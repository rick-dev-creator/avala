using Avala.Components.Canvases;
using Avala.Components.UI.Canvases;
using Avala.Rendering.Highlighting;
using Avala.Rendering.UI.Markdown;
using Avala.Rendering.UI.Source;
using Avala.Rendering.UI.Svg;
using Avala.Sdk;
using Avala.Sdk.UI;

namespace Avala.Rendering.UI;

public sealed class RenderingPlugin : IPlugin, IViewContributor
{
    public const string MermaidExplanation =
        "Avala does not draw Mermaid diagrams yet, so this shows the diagram's source.";

    public const string HtmlExplanation =
        "HTML canvases need an isolated web view, which Avala does not include yet, so this shows the page's source.";

    public PluginInfo Info { get; } = new("avala.rendering", "Canvas renderers");

    public void Register(IPluginRegistrar registrar)
    {
    }

    public void RegisterViews(IViewRegistrar views) =>
        views.AddCanvasRenderer(new MarkdownRenderer())
            .AddCanvasRenderer(new SvgRenderer())
            .AddCanvasRenderer(new SourceRenderer(CanvasMediaTypes.Mermaid, MermaidExplanation, SourceHighlighter.Mermaid))
            .AddCanvasRenderer(new SourceRenderer(CanvasMediaTypes.Html, HtmlExplanation, SourceHighlighter.Html));
}
