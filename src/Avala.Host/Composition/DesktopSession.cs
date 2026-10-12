using Avalonia;

namespace Avala.Host.Composition;

internal static class DesktopSession
{
    public const string X11Variable = "AVALA_X11";

    public static bool DrawsThroughWayland(bool linux, string waylandDisplay, string forcedX11) =>
        linux && waylandDisplay.Length > 0 && forcedX11 != "1";

    public static AppBuilder UseDesktopSession(this AppBuilder builder) =>
        DrawsThroughWayland(
            OperatingSystem.IsLinux(),
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? string.Empty,
            Environment.GetEnvironmentVariable(X11Variable) ?? string.Empty)
            ? builder.UseWaylandWithFallback().AfterPlatformServicesSetup(_ => DispatcherClock.Align())
            : builder;
}
