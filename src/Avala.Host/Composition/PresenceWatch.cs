using Avala.Runtime.Presence;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Avala.Host.Composition;

internal static class PresenceWatch
{
    public static void Follow(Window window, WindowPresence presence)
    {
        window.Activated += (_, _) => presence.Seen();
        window.Deactivated += (_, _) => presence.Seen();

        foreach (var input in new RoutedEvent[] { InputElement.KeyDownEvent, InputElement.PointerPressedEvent, InputElement.PointerMovedEvent, InputElement.PointerWheelChangedEvent })
        {
            window.AddHandler(input, new EventHandler<RoutedEventArgs>((_, _) => presence.Seen()), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }
}
