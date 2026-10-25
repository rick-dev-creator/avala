using System.Globalization;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Overview;

namespace Avala.Workbench.Tests.Overview;

public sealed class RelayLaneViewModelScripts
{
    private static readonly DateTimeOffset Five = new(2026, 10, 25, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ALaneReadsItsConnectionsTightestWindowAgainstTheThresholdAndWhenItResets()
    {
        var gauge = new RelayGauge(new ConnectionName("claude-work"), "Claude Code", new UsageLimit("5h", 0.91, Five));

        var lane = ViewModelScript.Given(new RelayLaneViewModel(gauge, 0.9, current: true)).ViewModel;

        Assert.Equal(("claude-work", "Claude Code", true), (lane.Name, lane.Harness, lane.IsCurrent));
        Assert.Equal($"91% · resets {Five.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}", lane.Meter.Reading);
        Assert.Equal((0.9, true), (lane.Meter.Threshold, lane.Meter.IsNearThreshold));
    }

    [Fact]
    public void ALaneWhoseConnectionReportedNoUsageSaysSo()
    {
        var gauge = new RelayGauge(new ConnectionName("claude-team"), string.Empty, Option<UsageLimit>.None);

        var lane = ViewModelScript.Given(new RelayLaneViewModel(gauge, Option<double>.None, current: false)).ViewModel;

        Assert.Equal(("0% · no usage reported", false), (lane.Meter.Reading, lane.Meter.HasThreshold));
    }
}
