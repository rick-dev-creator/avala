using Avalonia;
using Avalonia.Controls;

namespace Avala.Components.UI;

internal sealed partial class CanvasSurfaceView : UserControl
{
    private const double FocusedShare = 0.86;

    private TopLevel? host;

    public CanvasSurfaceView()
    {
        InitializeComponent();
        Focused.Opened += (_, _) => FocusedCard.Focus();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (TopLevel.GetTopLevel(this) is { } top)
        {
            host = top;
            Focused.PlacementTarget = top;
            Fit(top.ClientSize);
            top.PropertyChanged += Resized;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (host is { } top)
        {
            top.PropertyChanged -= Resized;
            host = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void Resized(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == TopLevel.ClientSizeProperty && sender is TopLevel top)
        {
            Fit(top.ClientSize);
        }
    }

    private void Fit(Size size)
    {
        FocusedCard.Width = size.Width * FocusedShare;
        FocusedCard.Height = size.Height * FocusedShare;
    }
}
