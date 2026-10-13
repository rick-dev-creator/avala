namespace Avala.Testing.UI;

public sealed record Retention(long Baseline, long Halfway, long After)
{
    public long Sustained => Math.Min(Halfway - Baseline, After - Halfway);
}

public static class Leaks
{
    public const string Collection = "Leaks";

    public const int Cycles = 20;

    public const long Tolerance = 2 * 1024 * 1024;

    public static long ManagedBytes()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return GC.GetTotalMemory(forceFullCollection: true);
    }

    public static async Task<Retention> ManagedAcrossAsync(Func<Task> cycle)
    {
        await cycle();
        var baseline = ManagedBytes();
        await CyclesAsync(cycle, (Cycles / 2) - 1);
        var halfway = ManagedBytes();
        await CyclesAsync(cycle, Cycles / 2);

        return new Retention(baseline, halfway, ManagedBytes());
    }

    private static async Task CyclesAsync(Func<Task> cycle, int count)
    {
        for (var round = 0; round < count; round++)
        {
            await cycle();
        }
    }
}
