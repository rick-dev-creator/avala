using Avala.Components.Status;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Avala.Components.UI;

public sealed class Portrait : TemplatedControl
{
    public static readonly StyledProperty<Mood> MoodProperty =
        AvaloniaProperty.Register<Portrait, Mood>(nameof(Mood));

    public Portrait() => Show(Mood);

    public Mood Mood
    {
        get => GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MoodProperty)
        {
            Show(change.GetNewValue<Mood>());
        }
    }

    private void Show(Mood mood)
    {
        PseudoClasses.Set(":focused", mood == Mood.Focused);
        PseudoClasses.Set(":asking", mood == Mood.Asking);
        PseudoClasses.Set(":worried", mood == Mood.Worried);
        PseudoClasses.Set(":pleased", mood == Mood.Pleased);
    }
}
