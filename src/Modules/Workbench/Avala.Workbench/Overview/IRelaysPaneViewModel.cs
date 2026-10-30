using Avala.Components.Graphs;
using Avala.Components.Meters;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal interface IRelaysPaneViewModel
{
    IReadProblemViewModel ReadProblem { get; }

    bool HasJourney { get; }

    string Caption { get; }

    string Headline { get; }

    string Subline { get; }

    bool IsWaiting { get; }

    RelayCourse Course { get; }

    IReadOnlyList<IRelayLaneViewModel> Lanes { get; }

    IReadOnlyList<IRelayLegViewModel> Legs { get; }

    string Began { get; }

    string Latest { get; }

    string Footer { get; }

    bool IsOpen { get; }

    IRelayCommand CloseCommand { get; }
}

internal interface IRelayLaneViewModel
{
    string Name { get; }

    string Harness { get; }

    IMeterViewModel Meter { get; }

    bool IsCurrent { get; }
}

internal interface IRelayLegViewModel
{
    string Title { get; }

    string Detail { get; }

    string When { get; }

    bool IsCurrent { get; }

    bool IsWaiting { get; }
}
