using Avalonia.Controls;
using Avalonia.Layout;
using Markdig.Extensions.Mathematics;
using Markdig.Renderers;
using Markdig.Syntax;
using MarkView.Avalonia.Extensions;
using MarkView.Avalonia.Rendering;

namespace Avala.Components.UI.Markdown;

public sealed class MathText : IMarkViewExtension, IMarkdownObjectRenderer
{
    public static MathText Instance { get; } = new();

    public void Register(AvaloniaRenderer renderer) => renderer.ObjectRenderers.Insert(0, this);

    public bool Accept(RendererBase renderer, Type objectType) => typeof(MathInline).IsAssignableFrom(objectType) || typeof(MathBlock).IsAssignableFrom(objectType);

    public void Write(RendererBase renderer, MarkdownObject objectToRender)
    {
        if (renderer is not AvaloniaRenderer avalonia)
        {
            return;
        }

        if (objectToRender is MathInline inline)
        {
            var formula = inline.Content.ToString();

            if (MathFormula.Parses(formula))
            {
                avalonia.WriteInline(new MathFormula(formula, display: false) { Classes = { "markdown-math" } });
            }
            else
            {
                avalonia.WriteInline(new Avalonia.Controls.Documents.Run(formula) { Classes = { "markdown-code-inline" } });
            }
        }
        else if (objectToRender is MathBlock block)
        {
            var formula = string.Join('\n', block.Lines.Lines?.Take(block.Lines.Count).Select(line => line.Slice.ToString()) ?? []);
            avalonia.WriteBlock(MathFormula.Parses(formula)
                ? new MathFormula(formula, display: true) { HorizontalAlignment = HorizontalAlignment.Center, Classes = { "markdown-math-block" } }
                : new SelectableTextBlock { Text = formula, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "markdown-math-source" } });
        }
    }
}
