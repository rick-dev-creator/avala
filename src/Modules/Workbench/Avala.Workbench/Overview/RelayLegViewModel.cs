using Avala.Sdk;

namespace Avala.Workbench.Overview;

internal sealed class RelayLegViewModel(RelayLeg leg, Option<double> used, bool last) : IRelayLegViewModel
{
    public string Title { get; } = RelayPhrases.Title(leg);

    public string Detail { get; } = RelayPhrases.Detail(leg, used);

    public string When { get; } = RelayPhrases.Clock(leg.From);

    public bool IsCurrent { get; } = last && leg.Kind != RelayLegKind.Ran;

    public bool IsWaiting { get; } = leg.Kind == RelayLegKind.Waiting;
}
