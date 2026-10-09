using System.Runtime.InteropServices;
using Avala.Sdk.Processes;

namespace Avala.Host.Composition;

internal interface ISystemMotion
{
    ValueTask<bool> PrefersReducedAsync(CancellationToken cancellationToken);
}

internal sealed class SystemMotion(IProcessRunner processes, Func<bool> windowsAnimates) : ISystemMotion
{
    public SystemMotion(IProcessRunner processes)
        : this(processes, WindowsAnimation.IsOn)
    {
    }

    public async ValueTask<bool> PrefersReducedAsync(CancellationToken cancellationToken) =>
        OperatingSystem.IsWindows() ? !windowsAnimates()
        : OperatingSystem.IsMacOS() ? await ReadAsync("defaults", ["read", "com.apple.universalaccess", "reduceMotion"], cancellationToken) == "1"
        : await ReadAsync("gsettings", ["get", "org.gnome.desktop.interface", "enable-animations"], cancellationToken) == "false";

    private async Task<string> ReadAsync(string program, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest(program, arguments), cancellationToken)).Match(
            outcome => outcome.Succeeded ? outcome.Output.Trim() : string.Empty,
            _ => string.Empty);
}

internal static class WindowsAnimation
{
    private const uint ClientAreaAnimation = 0x1042;

    public static bool IsOn() =>
        !SystemParametersInfoW(ClientAreaAnimation, 0, out var animates, 0) || animates != 0;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, out int value, uint update);
}
