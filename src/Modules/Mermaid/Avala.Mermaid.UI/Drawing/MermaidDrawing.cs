using Avala.Components.Canvases;
using Avala.Components.UI.Canvases;
using Avala.Mermaid.Translating;
using Avala.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Avala.Mermaid.UI.Drawing;

internal sealed class MermaidDrawing : ContentControl
{
    public const string Unrenderable = "This Mermaid diagram could not be drawn. Showing its source.";

    private readonly string source;
    private readonly MermaidDrawings drawings;
    private MermaidPalette shown = MermaidPalette.Dark;

    public MermaidDrawing(string source, MermaidDrawings drawings)
    {
        this.source = source;
        this.drawings = drawings;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        ActualThemeVariantChanged += (_, _) => _ = DrawAsync();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = DrawAsync();
    }

    private async Task DrawAsync()
    {
        var palette = new MermaidPalette(Color("SurfaceCanvasBrush"), Color("TextPrimaryBrush"), Color("AccentBrush"), Color("TextSecondaryBrush"));

        if (palette == shown && Content is not null)
        {
            return;
        }

        shown = palette;
        var drawn = await drawings.DrawAsync(source, palette);

        if (palette == shown)
        {
            Show(drawn);
        }
    }

    private void Show(Result<string, MermaidFailure> drawn)
    {
        Content = drawn.Match<object>(svg => new CanvasRendering(CanvasMediaTypes.Svg, svg, true, 1), _ => CanvasSource.Show(Unrenderable, source));
        RaiseEvent(new RoutedEventArgs(MermaidDrawn.Event));
    }

    private string Color(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is ISolidColorBrush brush
            ? $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}"
            : "#808080";
}
