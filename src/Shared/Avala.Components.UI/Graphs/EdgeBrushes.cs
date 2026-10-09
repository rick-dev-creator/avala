using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avala.Components.UI.Graphs;

public sealed class EdgeBrushes
{
    public static readonly AttachedProperty<IBrush> FlowingProperty =
        AvaloniaProperty.RegisterAttached<EdgeBrushes, Panel, IBrush>("Flowing", Brushes.Transparent);

    public static readonly AttachedProperty<IBrush> AttentionProperty =
        AvaloniaProperty.RegisterAttached<EdgeBrushes, Panel, IBrush>("Attention", Brushes.Transparent);

    public static readonly AttachedProperty<IBrush> WaitingProperty =
        AvaloniaProperty.RegisterAttached<EdgeBrushes, Panel, IBrush>("Waiting", Brushes.Transparent);

    public static readonly AttachedProperty<IBrush> QuietProperty =
        AvaloniaProperty.RegisterAttached<EdgeBrushes, Panel, IBrush>("Quiet", Brushes.Transparent);

    public static readonly AttachedProperty<double> PhaseProperty =
        AvaloniaProperty.RegisterAttached<EdgeBrushes, Panel, double>("Phase");

    static EdgeBrushes()
    {
        foreach (var property in new AvaloniaProperty[] { FlowingProperty, AttentionProperty, WaitingProperty, QuietProperty, PhaseProperty })
        {
            property.Changed.AddClassHandler<Panel>((panel, _) => EdgeLayer.Refresh(panel));
        }
    }

    private EdgeBrushes()
    {
    }

    public static IBrush GetFlowing(Panel panel) => panel.GetValue(FlowingProperty);

    public static void SetFlowing(Panel panel, IBrush value) => panel.SetValue(FlowingProperty, value);

    public static IBrush GetAttention(Panel panel) => panel.GetValue(AttentionProperty);

    public static void SetAttention(Panel panel, IBrush value) => panel.SetValue(AttentionProperty, value);

    public static IBrush GetWaiting(Panel panel) => panel.GetValue(WaitingProperty);

    public static void SetWaiting(Panel panel, IBrush value) => panel.SetValue(WaitingProperty, value);

    public static IBrush GetQuiet(Panel panel) => panel.GetValue(QuietProperty);

    public static void SetQuiet(Panel panel, IBrush value) => panel.SetValue(QuietProperty, value);

    public static double GetPhase(Panel panel) => panel.GetValue(PhaseProperty);

    public static void SetPhase(Panel panel, double value) => panel.SetValue(PhaseProperty, value);

    internal static EdgePens Of(Panel panel) => new(panel);
}
