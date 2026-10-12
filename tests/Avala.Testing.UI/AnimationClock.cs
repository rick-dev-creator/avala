using System.Diagnostics;
using System.Reflection;
using Avalonia;

namespace Avala.Testing.UI;

internal static class AnimationClock
{
    private static readonly Type Media = typeof(Visual).Assembly.GetType("Avalonia.Media.MediaContext")
        ?? throw new InvalidOperationException("Avalonia's media context was not found, so headless scripts cannot drive motion.");

    public static bool IsTicking =>
        Field(Media, "_clock").GetValue(Context()) is { } clock
        && clock.GetType().GetProperty("HasSubscriptions", BindingFlags.Public | BindingFlags.Instance)?.GetValue(clock) is true;

    public static void HoldStill() => Time().Stop();

    public static void Advance(TimeSpan by)
    {
        var time = Time();
        var elapsed = Field(typeof(Stopwatch), "_elapsed");
        elapsed.SetValue(time, (long)elapsed.GetValue(time)! + (long)(by.TotalSeconds * Stopwatch.Frequency));
    }

    public static void Pulse() =>
        (Media.GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Avalonia's media context cannot render, so headless scripts cannot drive motion."))
            .Invoke(Context(), null);

    private static object Context() =>
        Media.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? throw new InvalidOperationException("Avalonia's media context has no instance yet.");

    private static Stopwatch Time() =>
        Field(Media, "_time").GetValue(Context()) as Stopwatch
            ?? throw new InvalidOperationException("Avalonia's animation clock was not found, so headless scripts cannot hold motion still.");

    private static FieldInfo Field(Type type, string name) =>
        type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{type.Name}.{name} was not found, so headless scripts cannot drive motion.");
}
