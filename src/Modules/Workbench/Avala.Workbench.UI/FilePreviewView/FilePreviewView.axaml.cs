using Avalonia;
using Avalonia.Controls;

namespace Avala.Workbench.UI;

internal sealed partial class FilePreviewView : UserControl
{
    private const double SheetShare = 0.84;

    private TopLevel? host;

    public FilePreviewView()
    {
        InitializeComponent();
        Sheet.Opened += (_, _) => SheetCard.Focus();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (TopLevel.GetTopLevel(this) is { } top)
        {
            host = top;
            Sheet.PlacementTarget = top;
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
        SheetRoot.Width = size.Width;
        SheetRoot.Height = size.Height;
        SheetCard.Width = size.Width * SheetShare;
        SheetCard.Height = size.Height * SheetShare;
    }
}
