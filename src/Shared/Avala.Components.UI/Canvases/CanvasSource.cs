using System.Globalization;
using Avala.Components.Canvases;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace Avala.Components.UI.Canvases;

public static class CanvasSource
{
    public static Control Show(string note, string content) => Show(note, content, static text => [new Run(text)]);

    public static Control Show(string note, string content, Func<string, IEnumerable<Inline>> highlight)
    {
        var preview = content.Length <= CanvasRendering.SourcePreviewLength ? content : content[..CanvasRendering.SourcePreviewLength];
        var source = new SelectableTextBlock { Name = "CanvasSource", TextWrapping = TextWrapping.Wrap, Classes = { "mono" } };
        source.Inlines!.AddRange(highlight(preview));
        var panel = new StackPanel { Name = "CanvasFallback" };
        panel.Bind(StackPanel.SpacingProperty, panel.GetResourceObservable("Space2"));

        if (note.Length > 0)
        {
            panel.Children.Add(new TextBlock { Name = "CanvasNote", Text = note, TextWrapping = TextWrapping.Wrap, Classes = { "caption", "tertiary" } });
        }

        var well = new Border { Child = source, Classes = { "well" } };
        panel.Children.Add(well);

        if (content.Length > preview.Length)
        {
            var hidden = (content.Length - preview.Length).ToString("N0", CultureInfo.InvariantCulture);
            panel.Children.Add(new TextBlock { Name = "CanvasTruncated", Text = $"{hidden} more characters not shown", HorizontalAlignment = HorizontalAlignment.Left, Classes = { "caption", "tertiary" } });
        }

        return panel;
    }
}
