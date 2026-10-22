using Avalonia;
using Avalonia.Controls;

namespace Avala.Components.UI.Trimming;

public sealed class TrimmedTip
{
    public static readonly AttachedProperty<bool> IsOfferedProperty =
        AvaloniaProperty.RegisterAttached<TrimmedTip, TextBlock, bool>("IsOffered");

    static TrimmedTip() => IsOfferedProperty.Changed.AddClassHandler<TextBlock>((block, change) =>
    {
        if (change.GetNewValue<bool>())
        {
            block.LayoutUpdated += Offer;
        }
        else
        {
            block.LayoutUpdated -= Offer;
            ToolTip.SetTip(block, null);
        }
    });

    private TrimmedTip()
    {
    }

    public static bool GetIsOffered(TextBlock block) => block.GetValue(IsOfferedProperty);

    public static void SetIsOffered(TextBlock block, bool value) => block.SetValue(IsOfferedProperty, value);

    private static bool IsCut(TextBlock block) =>
        block.TextLayout.TextLines.Any(line => line.HasCollapsed);

    private static void Offer(object? sender, EventArgs args)
    {
        if (sender is TextBlock block && block.IsArrangeValid)
        {
            var tip = IsCut(block) ? block.Text : null;

            if (!Equals(ToolTip.GetTip(block), tip))
            {
                ToolTip.SetTip(block, tip);
            }
        }
    }
}
