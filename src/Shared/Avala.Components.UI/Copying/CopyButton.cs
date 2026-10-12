using Avala.Components.UI.Elapsed;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Path = Avalonia.Controls.Shapes.Path;

namespace Avala.Components.UI.Copying;

public sealed class CopyButton : Button
{
    public static readonly StyledProperty<bool> IsCopiedProperty =
        AvaloniaProperty.Register<CopyButton, bool>(nameof(IsCopied));

    public static readonly TimeSpan Confirmation = TimeSpan.FromSeconds(1.5);

    private IDisposable? confirming;

    public CopyButton()
    {
        Classes.Add("glyph");
        Classes.Add("mini");
        Classes.Add("copy");
        Content = new Panel { Children = { Icon("Copy", "IconCopy"), Icon("Copied", "IconCheck") } };
    }

    public bool IsCopied
    {
        get => GetValue(IsCopiedProperty);
        set => SetValue(IsCopiedProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsCopiedProperty)
        {
            Confirm(IsCopied);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Confirm(false);
    }

    private void Confirm(bool copied)
    {
        confirming?.Dispose();
        confirming = null;
        PseudoClasses.Set(":copied", copied);

        if (copied)
        {
            confirming = ElapsedText.GetClock(this).CreateTimer(
                _ => Dispatcher.UIThread.Post(() => PseudoClasses.Set(":copied", false)),
                null,
                Confirmation,
                Timeout.InfiniteTimeSpan);
        }
    }

    private Path Icon(string name, string geometry)
    {
        var icon = new Path { Name = name, Classes = { "icon" } };
        icon.Bind(Path.DataProperty, this.GetResourceObservable(geometry));

        return icon;
    }
}
