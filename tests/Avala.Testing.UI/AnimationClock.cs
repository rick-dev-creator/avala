using System.Diagnostics;
using System.Reflection;
using Avalonia;

namespace Avala.Testing.UI;

internal static class AnimationClock
{
    public static void HoldStill() => Time().Stop();

    private static Stopwatch Time()
    {
        var media = typeof(Visual).Assembly.GetType("Avalonia.Media.MediaContext");
        var context = media?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

        return media?.GetField("_time", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(context) as Stopwatch
            ?? throw new InvalidOperationException("Avalonia's animation clock was not found, so headless scripts cannot hold motion still.");
    }
}
