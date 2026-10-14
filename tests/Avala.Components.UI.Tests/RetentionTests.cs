using Avala.Testing.UI;

namespace Avala.Components.UI.Tests;

public sealed class RetentionTests
{
    private const long Baseline = 115_158_208;

    private static readonly long[] Sawtooth = [2_885_728, 5_621_800, 7_999_456, 10_324_128, 12_633_480, 14_951_656, 17_279_472, -22_592];

    private static readonly long[] Step = [145_784, -1_102_928, 1_800_352, -1_135_424, -1_135_448, -1_153_480, -1_137_056, 1_626_432, 1_808_576, 1_825_432, 1_821_448, 1_808_704, 1_813_224, 1_805_280];

    [Fact]
    public void MemoryThatKeepsReturningToItsBaselineIsBoundedHoweverHighItsPeaks()
    {
        var retention = Measured(Cycled(Sawtooth));

        Assert.True(retention.IsBounded, retention.ToString());
    }

    [Fact]
    public void MemoryThatSettlesOnceAtAHigherLevelIsBounded()
    {
        var retention = Measured([.. Step, .. Enumerable.Repeat(1_813_480L, (Leaks.Cycles - 1) - Step.Length)]);

        Assert.True(retention.IsBounded, retention.ToString());
    }

    [Fact]
    public void MemoryWhoseFloorKeepsRisingIsALeakEvenWhenItsReadingsWobble()
    {
        var rising = Enumerable.Range(1, Leaks.Cycles - 1).Select(cycle => (cycle * 300_000L) + (cycle % 2 == 0 ? 240_000 : 0)).ToArray();

        var retention = Measured(rising);

        Assert.False(retention.IsBounded, retention.ToString());
        Assert.Contains("over a 2,097,152 tolerance", retention.ToString(), StringComparison.Ordinal);
    }

    private static long[] Cycled(long[] pattern) => [.. Enumerable.Range(0, Leaks.Cycles - 1).Select(cycle => pattern[cycle % pattern.Length])];

    private static Retention Measured(long[] fromBaseline)
    {
        long[] cycles = [.. fromBaseline.Select(bytes => Baseline + bytes)];

        return new Retention(Baseline, cycles[(cycles.Length / 2) - 1], cycles[^1]) { Cycles = cycles };
    }
}
