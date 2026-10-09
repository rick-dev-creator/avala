using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkView.Avalonia;

namespace Avala.Components.UI.Markdown;

public static class OfflineMarkdown
{
    public static MarkdownPipeline WithoutImages { get; } = Pipeline(_ => false);

    public static MarkdownPipeline Pipeline(Func<string, bool> keepImage)
    {
        var builder = new MarkdownPipelineBuilder().UseSupportedExtensions();
        builder.DocumentProcessed += document => WithoutRemoteImages(document, keepImage);

        return builder.Build();
    }

    private static void WithoutRemoteImages(MarkdownDocument document, Func<string, bool> keepImage)
    {
        foreach (var image in document.Descendants<LinkInline>().Where(link => link.IsImage && !keepImage(link.Url ?? string.Empty)).ToList())
        {
            var alt = string.Concat(image.Descendants<LiteralInline>().Select(literal => literal.Content.ToString()));
            image.ReplaceBy(new LiteralInline(alt.Length > 0 ? $"[{alt}]" : "[image]"), copyChildren: false);
        }
    }
}
