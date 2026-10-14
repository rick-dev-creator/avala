using System.Globalization;

namespace Avala.Testing.UI;

public sealed record Retention(long Baseline, long Halfway, long After)
{
    public IReadOnlyList<long> Cycles { get; init; } = [];

    public long Sustained
    {
        get
        {
            long[] readings = [Baseline, .. Cycles];
            var third = readings.Length / 3;
            var first = Floor(readings.Take(third));
            var second = Floor(readings.Skip(third).Take(third));
            var last = Floor(readings.Skip(2 * third));

            return Math.Min(second - first, last - second);
        }
    }

    public bool IsBounded => Sustained <= Leaks.Tolerance;

    private static long Floor(IEnumerable<long> readings) => readings.DefaultIfEmpty(0).Min();

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"the floor of the managed memory rose by at least {Sustained:N0} bytes from each third of the cycles to the next, over a {Leaks.Tolerance:N0} tolerance; baseline {Baseline:N0}, halfway {Halfway:N0}, after {After:N0}; after each cycle [{string.Join(", ", Cycles.Select(bytes => (bytes - Baseline).ToString("N0", CultureInfo.InvariantCulture)))}] from the baseline");
}

public static class Leaks
{
    public const string Collection = "Leaks";

    public const int Cycles = 30;

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
