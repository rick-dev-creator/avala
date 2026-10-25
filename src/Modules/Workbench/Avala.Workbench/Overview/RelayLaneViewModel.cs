using Avala.Components.Meters;
using Avala.Sdk;

namespace Avala.Workbench.Overview;

internal sealed class RelayLaneViewModel : IRelayLaneViewModel
{
    public RelayLaneViewModel(RelayGauge gauge, Option<double> threshold, bool current)
    {
        Name = gauge.Connection.Value;
        Harness = gauge.Harness;
        IsCurrent = current;
        var meter = new MeterViewModel(string.Empty, threshold);
        gauge.Tightest.Match(
            limit =>
            {
                meter.Show(limit.UsedFraction, RelayPhrases.Reading(limit.ResetsAt));

                return true;
            },
            () =>
            {
                meter.Show(0, "no usage reported");

                return false;
            });
        Meter = meter;
    }

    public string Name { get; }

    public string Harness { get; }

    public IMeterViewModel Meter { get; }

    public bool IsCurrent { get; }
}
