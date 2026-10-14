using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Syntax;
using MarkView.Avalonia.Extensions;
using MarkView.Avalonia.Rendering;
using MarkView.Avalonia.Rendering.Blocks;

namespace Avala.Components.UI.Markdown;

public sealed class ScrollingTables : IMarkViewExtension, IMarkdownObjectRenderer
{
    private static readonly TableRenderer Tables = new();

    public static ScrollingTables Instance { get; } = new();

    public void Register(AvaloniaRenderer renderer) => renderer.ReplaceOrAdd<TableRenderer>(this);

    public bool Accept(RendererBase renderer, Type objectType) => typeof(Table).IsAssignableFrom(objectType);

    public void Write(RendererBase renderer, MarkdownObject objectToRender)
    {
        if (renderer is not AvaloniaRenderer avalonia || objectToRender is not Table table)
        {
            return;
        }

        var holder = new StackPanel();
        avalonia.Push(holder);
        Tables.Write(avalonia, table);
        avalonia.Pop();

        foreach (var drawn in holder.Children.ToList())
        {
            holder.Children.Remove(drawn);
            avalonia.WriteBlock(new ScrollViewer
            {
                Content = drawn,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Classes = { "markdown-table-scroller" },
            });
        }
    }
}
