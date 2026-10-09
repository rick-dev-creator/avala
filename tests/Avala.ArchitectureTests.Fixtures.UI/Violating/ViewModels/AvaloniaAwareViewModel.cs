using Avalonia.Media;

namespace Avala.Fixtures.Violating.ViewModels;

public sealed class AvaloniaAwareViewModel
{
    public IBrush Highlight { get; } = Brushes.Orange;
}
