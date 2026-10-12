using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Avala.Mermaid.UI.Drawing;

public static class MermaidDrawn
{
    public static readonly RoutedEvent<RoutedEventArgs> Event =
        RoutedEvent.Register<ContentControl, RoutedEventArgs>("MermaidDrawn", RoutingStrategies.Bubble);
}
