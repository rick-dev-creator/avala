using Avala.Components.Canvases;
using Avala.Mermaid.Translating;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Avala.Mermaid.UI.Drawing;

internal sealed class MermaidDrawing : ContentControl
{
    private readonly string source;

    public MermaidDrawing(string source)
    {
        this.source = source;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        ActualThemeVariantChanged += (_, _) => Draw();
    }

    public string Svg { get; private set; } = string.Empty;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Draw();
    }

    private void Draw()
    {
        var palette = new MermaidPalette(Color("SurfaceCanvasBrush"), Color("TextPrimaryBrush"), Color("AccentBrush"), Color("TextSecondaryBrush"));

        Svg = MermaidSvg.Draw(source, palette).Match(svg => svg, () => string.Empty);
        Content = Svg.Length > 0 ? new CanvasRendering(CanvasMediaTypes.Svg, Svg, true, 1) : null;
    }

    private string Color(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is ISolidColorBrush brush
            ? $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}"
            : "#808080";
}
