using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using CSharpMath.SkiaSharp;
using SkiaSharp;

namespace Avala.Components.UI.Markdown;

public sealed class MathFormula : Control
{
    private readonly string latex;
    private readonly bool display;
    private MathPainter? painter;
    private Size drawn;

    static MathFormula()
    {
        AffectsMeasure<MathFormula>(TextElement.FontSizeProperty);
        AffectsRender<MathFormula>(TextElement.ForegroundProperty);
    }

    public MathFormula(string latex, bool display)
    {
        this.latex = latex;
        this.display = display;
    }

    public string LaTeX => latex;

    public static bool Parses(string latex) => new MathPainter { LaTeX = latex }.ErrorMessage is null;

    public bool IsTypeset => Painter().ErrorMessage is null && drawn.Width > 0;

    public override void Render(DrawingContext context)
    {
        if (drawn.Width <= 0 || Painter().ErrorMessage is not null)
        {
            return;
        }

        var color = TextElement.GetForeground(this) is ISolidColorBrush solid ? solid.Color : Colors.Gray;
        context.Custom(new Drawing(new Rect(drawn), Painter(), new SKColor(color.R, color.G, color.B, color.A)));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var math = Painter();
        math.FontSize = (float)TextElement.GetFontSize(this);
        var size = math.Measure((float)(double.IsInfinity(availableSize.Width) ? 10_000 : availableSize.Width));
        drawn = math.ErrorMessage is null ? new Size(Math.Ceiling(size.Width), Math.Ceiling(size.Height)) : default;

        return drawn;
    }

    private MathPainter Painter() =>
        painter ??= new MathPainter { LaTeX = latex, LineStyle = display ? CSharpMath.Atom.LineStyle.Display : CSharpMath.Atom.LineStyle.Text, DisplayErrorInline = false, AntiAlias = true };

    private sealed class Drawing(Rect bounds, MathPainter painter, SKColor color) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
            {
                return;
            }

            using var lease = feature.Lease();
            painter.TextColor = color;
            painter.Draw(lease.SkCanvas, 0f, painter.Display is { } shown ? shown.Ascent : 0f);
        }

        public void Dispose()
        {
        }
    }
}
