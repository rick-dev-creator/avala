using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace Avala.Components.UI;

public sealed class Fold : HeaderedContentControl
{
    public static readonly StyledProperty<string> FactProperty =
        AvaloniaProperty.Register<Fold, string>(nameof(Fact), string.Empty);

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<Fold, bool>(nameof(IsExpanded), defaultBindingMode: BindingMode.TwoWay);

    public Fold() => PseudoClasses.Set(":expanded", IsExpanded);

    public string Fact
    {
        get => GetValue(FactProperty);
        set => SetValue(FactProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsExpandedProperty)
        {
            PseudoClasses.Set(":expanded", change.GetNewValue<bool>());
        }
    }
}
