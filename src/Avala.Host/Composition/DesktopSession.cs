using Avalonia;

namespace Avala.Host.Composition;

internal static class DesktopSession
{
    public const string X11Variable = "AVALA_X11";

    public const string DesktopId = "avala";

    public static string AppIdOf(string flatpakId) => flatpakId.Length > 0 ? flatpakId : DesktopId;

    public static bool DrawsThroughWayland(bool linux, string waylandDisplay, string forcedX11) =>
        linux && waylandDisplay.Length > 0 && forcedX11 != "1";

    public static AppBuilder UseDesktopSession(this AppBuilder builder)
    {
        var appId = AppIdOf(Environment.GetEnvironmentVariable("FLATPAK_ID") ?? string.Empty);
        var named = builder.With(new X11PlatformOptions { WmClass = appId });

        return DrawsThroughWayland(
            OperatingSystem.IsLinux(),
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? string.Empty,
            Environment.GetEnvironmentVariable(X11Variable) ?? string.Empty)
            ? named.UseWaylandWithFallback().With(new WaylandPlatformOptions { AppId = appId }).AfterPlatformServicesSetup(_ => DispatcherClock.Align())
            : named;
    }
}
