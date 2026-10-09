using System.Globalization;
using Avala.Components.Graphs;
using Avala.Components.Status;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Avala.Components.UI.Graphs;

public sealed class Graph
{
    public static readonly AttachedProperty<EdgeKind> EdgeProperty =
        AvaloniaProperty.RegisterAttached<Graph, Control, EdgeKind>("Edge");

    public static readonly AttachedProperty<int> DepthProperty =
        AvaloniaProperty.RegisterAttached<Graph, Control, int>("Depth", 1);

    public static readonly AttachedProperty<double> WeightProperty =
        AvaloniaProperty.RegisterAttached<Graph, Control, double>("Weight");

    static Graph()
    {
        EdgeProperty.Changed.AddClassHandler<Control>((control, _) => Invalidate(control, false));
        DepthProperty.Changed.AddClassHandler<Control>((control, _) => Invalidate(control, true));
        WeightProperty.Changed.AddClassHandler<Control>((control, _) => Invalidate(control, true));
    }

    private Graph()
    {
    }

    public static IValueConverter EdgeOfStatus { get; } = new FuncValueConverter<StatusKind, EdgeKind>(EdgeKinds.Of);

    public static IValueConverter Many { get; } = new FuncValueConverter<int, bool>(count => count > 1);

    public static IValueConverter Degrees { get; } = new FuncValueConverter<double, double>(fraction => Math.Clamp(fraction, 0, 1) * 360);

    public static IValueConverter Initials { get; } = new FuncValueConverter<string, string>(name =>
        string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : string.Concat(name.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries).Take(2).Select(word => char.ToUpper(word[0], CultureInfo.InvariantCulture))));

    public static EdgeKind GetEdge(Control control) => control.GetValue(EdgeProperty);

    public static void SetEdge(Control control, EdgeKind value) => control.SetValue(EdgeProperty, value);

    public static int GetDepth(Control control) => control.GetValue(DepthProperty);

    public static void SetDepth(Control control, int value) => control.SetValue(DepthProperty, value);

    public static double GetWeight(Control control) => control.GetValue(WeightProperty);

    public static void SetWeight(Control control, double value) => control.SetValue(WeightProperty, value);

    internal static T Read<T>(Control child, AttachedProperty<T> property) =>
        child is ContentPresenter { Child: { } inner } && !child.IsSet(property) ? inner.GetValue(property) : child.GetValue(property);

    private static void Invalidate(Control control, bool layout)
    {
        foreach (var panel in control.GetVisualAncestors().OfType<Panel>().Where(panel => panel is HubGraphPanel or TreeGraphPanel or ShareBar))
        {
            if (layout)
            {
                panel.InvalidateMeasure();
            }

            EdgeLayer.Refresh(panel);
        }
    }
}

internal sealed class EdgePens(Panel panel)
{
    private const double Thickness = 1.2;

    public IPen For(EdgeKind kind) => kind switch
    {
        EdgeKind.Flowing => new Pen(EdgeBrushes.GetFlowing(panel), Thickness, new DashStyle([4 / Thickness, 6 / Thickness], EdgeBrushes.GetPhase(panel) / Thickness)),
        EdgeKind.Attention => new Pen(EdgeBrushes.GetAttention(panel), Thickness),
        EdgeKind.Waiting => new Pen(EdgeBrushes.GetWaiting(panel), Thickness, new DashStyle([2 / Thickness, 4 / Thickness], 0)),
        _ => new Pen(EdgeBrushes.GetQuiet(panel), Thickness),
    };
}
