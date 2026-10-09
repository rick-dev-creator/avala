using Avala.Components.UI.Canvases;
using Avala.Components.UI.Markdown;
using Avala.Rendering.Offer;
using Avala.Rendering.Sanitizing;
using Avala.Sdk;
using Avalonia.Controls;
using Avalonia.Layout;
using Markdig;

namespace Avala.Rendering.UI.Markdown;

internal sealed class MarkdownRenderer : ICanvasRenderer
{
    private readonly MarkdownPipeline pipeline = OfflineMarkdown.Pipeline(EmbeddedImages.Allow);

    public string MediaType => RenderedFormats.Markdown.MediaType;

    public Option<Control> Render(string content) =>
        new MarkdownText(pipeline) { Name = "Markdown", Markdown = content, VerticalAlignment = VerticalAlignment.Top };
}
