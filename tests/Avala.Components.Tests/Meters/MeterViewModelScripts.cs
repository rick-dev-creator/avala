using Avala.Components.Meters;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Components.Tests.Meters;

public sealed class MeterViewModelScripts
{
    [Fact]
    public void AnEmptyMeterReadsZero() =>
        ViewModelScript.Given(new MeterViewModel("claude-personal · 5h", Option<double>.None))
            .Then(meter => Assert.Equal(("0%", 0d, false), (meter.Reading, meter.Fraction, meter.HasThreshold)));

    [Fact]
    public void ShowingAUsageReadsItAsAPercentWithItsDetail() =>
        ViewModelScript.Given(new MeterViewModel("claude-work · 5h", 0.9))
            .When(meter => meter.Show(0.88, "resets 16:20"))
            .ThenNotified(nameof(MeterViewModel.Fraction), nameof(MeterViewModel.Reading), nameof(MeterViewModel.IsNearThreshold))
            .Then(meter => Assert.Equal(("88% · resets 16:20", 0.88), (meter.Reading, meter.Fraction)));

    [Theory]
    [InlineData(-0.2, 0, "0%")]
    [InlineData(1.4, 1, "100%")]
    public void AUsageOutsideTheScaleIsClamped(double usage, double fraction, string reading) =>
        ViewModelScript.Given(new MeterViewModel("Memory", Option<double>.None))
            .When(meter => meter.Show(usage))
            .Then(meter => Assert.Equal((fraction, reading), (meter.Fraction, meter.Reading)));

    [Theory]
    [InlineData(0.31, false)]
    [InlineData(0.79, false)]
    [InlineData(0.81, true)]
    [InlineData(0.88, true)]
    [InlineData(0.97, true)]
    public void TheMeterTurnsToAttentionOnlyNearItsThreshold(double usage, bool near) =>
        ViewModelScript.Given(new MeterViewModel("claude-work · 5h", 0.9))
            .When(meter => meter.Show(usage))
            .Then(meter => Assert.Equal(near, meter.IsNearThreshold));

    [Fact]
    public void AMeterWithoutAThresholdIsNeverNearIt() =>
        ViewModelScript.Given(new MeterViewModel("CPU", Option<double>.None))
            .When(meter => meter.Show(1))
            .Then(meter => Assert.False(meter.IsNearThreshold));

    [Fact]
    public void TheDesignTimeMeterIsAConnectionCloseToItsLimit()
    {
        var meter = new DesignMeterViewModel();

        Assert.True(meter.IsNearThreshold && meter.Fraction < meter.Threshold);
    }
}
