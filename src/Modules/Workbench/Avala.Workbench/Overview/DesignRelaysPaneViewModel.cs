using Avala.Components.Graphs;
using Avala.Components.Meters;
using Avala.Sdk;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal sealed class DesignRelaysPaneViewModel : IRelaysPaneViewModel
{
    public DesignRelaysPaneViewModel()
        : this(false)
    {
    }

    public DesignRelaysPaneViewModel(bool waiting)
    {
        IsWaiting = waiting;
        Headline = waiting ? "Waiting for claude-work to reset" : "Running on claude-max";
        Subline = waiting
            ? "Every connection is near its limit · resumes 17:00, compacted first"
            : "Took over from claude-team at 90% · 15:48";
        Course = waiting ? DesignRelays.Waiting : DesignRelays.Running;
        Lanes =
        [
            new DesignRelayLaneViewModel("claude-work", "Claude Code · Max", Meter(string.Empty, waiting ? 0.97 : 0.91, "resets 17:00"), waiting),
            new DesignRelayLaneViewModel("claude-team", "Claude Code · Team", Meter(string.Empty, 0.92, "resets 18:10"), false),
            new DesignRelayLaneViewModel("claude-max", "Claude Code · Max 20x", Meter(string.Empty, waiting ? 0.95 : 0.38, "resets 19:30"), !waiting),
        ];
        Legs = waiting
            ?
            [
                new DesignRelayLegViewModel("claude-work", "Ran 1h 52m · wound down at 90%", "13:10", false, false),
                new DesignRelayLegViewModel("claude-team", "Ran 2h 38m · wound down at 90%", "15:04", false, false),
                new DesignRelayLegViewModel("claude-max", "Ran 1h 26m · capped at 97%", "17:42", false, false),
                new DesignRelayLegViewModel("Waiting for claude-work", "Compacts, then resumes at its reset", "17:44", true, true),
            ]
            :
            [
                new DesignRelayLegViewModel("claude-work", "Ran 1h 52m · wound down at 90%", "13:10", false, false),
                new DesignRelayLegViewModel("claude-team", "Ran 2h 38m · wound down at 90%", "15:04", false, false),
                new DesignRelayLegViewModel("claude-max", "Running · 38% of its window", "15:48", true, false),
            ];
    }

    public IReadProblemViewModel ReadProblem { get; } = new DesignReadProblemViewModel();

    public bool HasJourney => true;

    public string Caption => "3 connections · 2 handoffs";

    public string Headline { get; }

    public string Subline { get; }

    public bool IsWaiting { get; }

    public RelayCourse Course { get; }

    public IReadOnlyList<IRelayLaneViewModel> Lanes { get; }

    public IReadOnlyList<IRelayLegViewModel> Legs { get; }

    public string Began => "13:10";

    public string Latest => "now";

    public string Footer => "Relay across connections · prepare at 90% · hard cap 97%";

    public bool IsOpen => true;

    public IRelayCommand CloseCommand { get; } = new RelayCommand(() => { });

    private static MeterViewModel Meter(string label, double fraction, string detail)
    {
        var meter = new MeterViewModel(label, Option<double>.Some(0.9));
        meter.Show(fraction, Option<string>.Some(detail));

        return meter;
    }
}

internal sealed record DesignRelayLaneViewModel(string Name, string Harness, IMeterViewModel Meter, bool IsCurrent) : IRelayLaneViewModel;

internal sealed record DesignRelayLegViewModel(string Title, string Detail, string When, bool IsCurrent, bool IsWaiting) : IRelayLegViewModel;

internal static class DesignRelays
{
    public static RelayCourse Running { get; } = new(
        3,
        [
            new RelayStretch(0, 0, 0.3, RelayStretchKind.Ran),
            new RelayStretch(1, 0.3, 0.72, RelayStretchKind.Ran),
            new RelayStretch(2, 0.72, 1, RelayStretchKind.Running),
        ],
        [
            new RelayStop(0, 0.27, RelayStopKind.WindingDown),
            new RelayStop(1, 0.69, RelayStopKind.WindingDown),
            new RelayStop(1, 0.3, RelayStopKind.HandedOff),
            new RelayStop(2, 0.72, RelayStopKind.HandedOff),
        ]);

    public static RelayCourse Waiting { get; } = new(
        3,
        [
            new RelayStretch(0, 0, 0.24, RelayStretchKind.Ran),
            new RelayStretch(1, 0.24, 0.58, RelayStretchKind.Ran),
            new RelayStretch(2, 0.58, 0.82, RelayStretchKind.Ran),
            new RelayStretch(0, 0.82, 1, RelayStretchKind.Waiting),
        ],
        [
            new RelayStop(0, 0.21, RelayStopKind.WindingDown),
            new RelayStop(1, 0.24, RelayStopKind.HandedOff),
            new RelayStop(1, 0.55, RelayStopKind.WindingDown),
            new RelayStop(2, 0.58, RelayStopKind.HandedOff),
            new RelayStop(2, 0.8, RelayStopKind.Capped)
        ]);
}
