using Avala.Components.Status;

namespace Avala.Components.Graphs;

public enum EdgeKind
{
    Quiet,
    Flowing,
    Attention,
    Waiting,
    Failure,
}

public static class EdgeKinds
{
    public static EdgeKind Of(StatusKind status) => status switch
    {
        StatusKind.Working or StatusKind.Checking => EdgeKind.Flowing,
        StatusKind.NeedsYou => EdgeKind.Attention,
        StatusKind.Held => EdgeKind.Waiting,
        StatusKind.Failed => EdgeKind.Failure,
        _ => EdgeKind.Quiet,
    };
}
