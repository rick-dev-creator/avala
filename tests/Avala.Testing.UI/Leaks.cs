using System.Globalization;

namespace Avala.Testing.UI;

public sealed record Retention(long Baseline, long Halfway, long After)
{
    public IReadOnlyList<long> Cycles { get; init; } = [];

    public long Sustained => Math.Min(Halfway - Baseline, After - Halfway);

    public bool IsBounded => Sustained <= Leaks.Tolerance;

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"sustained {Sustained:N0} bytes over a {Leaks.Tolerance:N0} tolerance; baseline {Baseline:N0}, halfway {Halfway:N0}, after {After:N0}; after each cycle [{string.Join(", ", Cycles.Select(bytes => (bytes - Baseline).ToString("N0", CultureInfo.InvariantCulture)))}] from the baseline");
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
        var measured = new List<long>();
        await CyclesAsync(cycle, (Cycles / 2) - 1, measured);
        var halfway = measured[^1];
        await CyclesAsync(cycle, Cycles / 2, measured);

        return new Retention(baseline, halfway, measured[^1]) { Cycles = measured };
    }

    private static async Task CyclesAsync(Func<Task> cycle, int count, List<long> measured)
    {
        for (var round = 0; round < count; round++)
        {
            await cycle();
            measured.Add(ManagedBytes());
        }
    }
}
