using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Avala.Components.UI.Graphs;

internal sealed class EdgeLayer : Control
{
    private readonly Panel owner;
    private readonly Action<DrawingContext, EdgePens> draw;

    public EdgeLayer(Panel owner, Action<DrawingContext, EdgePens> draw)
    {
        this.owner = owner;
        this.draw = draw;
        IsHitTestVisible = false;
        ZIndex = int.MinValue;
    }

    public override void Render(DrawingContext context) => draw(context, EdgeBrushes.Of(owner));

    public static void Refresh(Visual panel)
    {
        foreach (var layer in panel.GetVisualChildren().OfType<EdgeLayer>())
        {
            layer.InvalidateVisual();
        }
    }

    public void Fit(Size size)
    {
        Measure(size);
        Arrange(new Rect(size));
        InvalidateVisual();
    }
}
