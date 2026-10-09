using Avala.Components.Canvases;
using Avala.Components.UI.Canvases;
using Avala.Rendering.Sanitizing;
using Avala.Sdk;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkView.Avalonia;

namespace Avala.Rendering.UI.Markdown;

internal sealed class MarkdownRenderer : ICanvasRenderer
{
    private static readonly Uri Base = new("avares://Avala.Rendering.UI/");
    private static readonly Uri Styles = new("Markdown/MarkdownStyles.axaml", UriKind.Relative);

    private readonly MarkdownPipeline pipeline = OfflinePipeline();

    public bool Renders(string mediaType) => mediaType == CanvasMediaTypes.Markdown;

    public Option<Control> Render(string content)
    {
        var viewer = new MarkdownViewer { Name = "Markdown", Pipeline = pipeline, Markdown = content };
        viewer.LinkClicked += (_, link) => link.Handled = true;

        return new Border
        {
            VerticalAlignment = VerticalAlignment.Top,
            Child = viewer,
            Styles = { new StyleInclude(Base) { Source = Styles } },
        };
    }

    private static MarkdownPipeline OfflinePipeline()
    {
        var builder = new MarkdownPipelineBuilder().UseSupportedExtensions();
        builder.DocumentProcessed += WithoutRemoteImages;

        return builder.Build();
    }

    private static void WithoutRemoteImages(MarkdownDocument document)
    {
        foreach (var image in document.Descendants<LinkInline>().Where(link => link.IsImage && !EmbeddedImages.Allow(link.Url ?? string.Empty)).ToList())
        {
            var alt = string.Concat(image.Descendants<LiteralInline>().Select(literal => literal.Content.ToString()));
            image.ReplaceBy(new LiteralInline(alt.Length > 0 ? $"[{alt}]" : "[image]"), copyChildren: false);
        }
    }
}
