using Avalonia;
using Avalonia.VisualTree;

namespace Avala.Components.UI.Theme;

public sealed class Sightline
{
    private readonly List<Visual> chain;
    private readonly Action changed;

    public Sightline(Visual visual, Action changed)
    {
        chain = [visual, .. visual.GetVisualAncestors()];
        this.changed = changed;

        foreach (var link in chain)
        {
            link.PropertyChanged += OnChanged;
        }
    }

    public bool IsClear => chain.TrueForAll(link => link.IsVisible);

    public void Close()
    {
        foreach (var link in chain)
        {
            link.PropertyChanged -= OnChanged;
        }

        chain.Clear();
    }

    private void OnChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == Visual.IsVisibleProperty)
        {
            changed();
        }
    }
}
