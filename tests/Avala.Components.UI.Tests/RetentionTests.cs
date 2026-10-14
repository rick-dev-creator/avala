using Avala.Testing.UI;

namespace Avala.Components.UI.Tests;

public sealed class RetentionTests
{
    private const long Baseline = 115_158_208;

    [Fact]
    public void MemoryThatKeepsReturningToItsBaselineIsBoundedHoweverHighItsPeaks()
    {
        long[] sawtooth = [2_885_728, 5_621_800, 7_999_456, 10_324_128, 12_633_480, 14_951_656, 17_279_472, -22_592, 2_331_216, 4_662_688, 6_996_216, 9_399_936, 11_698_288, 14_008_512, 16_353_240, -36_016, 2_273_176, 4_600_008, 6_938_256];

        var retention = Measured(sawtooth);

        Assert.True(retention.IsBounded, retention.ToString());
    }

    [Fact]
    public void MemoryWhoseFloorKeepsRisingIsALeakEvenWhenItsReadingsWobble()
    {
        var rising = Enumerable.Range(1, 19).Select(cycle => (cycle * 300_000L) + (cycle % 2 == 0 ? 240_000 : 0)).ToArray();

        var retention = Measured(rising);

        Assert.False(retention.IsBounded, retention.ToString());
        Assert.Contains("over a 2,097,152 tolerance", retention.ToString(), StringComparison.Ordinal);
    }

    private static Retention Measured(long[] fromBaseline)
    {
        long[] cycles = [.. fromBaseline.Select(bytes => Baseline + bytes)];

        return new Retention(Baseline, cycles[(cycles.Length / 2) - 1], cycles[^1]) { Cycles = cycles };
    }
}
