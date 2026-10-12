namespace Avala.Testing.UI;

public static class Leaks
{
    public const string Collection = "Leaks";

    public const int Cycles = 20;

    public const long Tolerance = 4 * 1024 * 1024;

    public static long ManagedBytes()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return GC.GetTotalMemory(forceFullCollection: true);
    }

    public static async Task<(long Baseline, long After)> ManagedAcrossAsync(Func<Task> cycle)
    {
        await cycle();
        var baseline = ManagedBytes();

        for (var round = 1; round < Cycles; round++)
        {
            await cycle();
        }

        return (baseline, ManagedBytes());
    }
}
