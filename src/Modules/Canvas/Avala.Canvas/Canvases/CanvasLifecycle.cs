using Stateless;

namespace Avala.Canvas.Canvases;

internal static class CanvasLifecycle
{
    public static StateMachine<CanvasState, CanvasTrigger> Create(Func<CanvasState> read, Action<CanvasState> write)
    {
        var machine = new StateMachine<CanvasState, CanvasTrigger>(read, write);

        machine.Configure(CanvasState.Streaming)
            .Permit(CanvasTrigger.Complete, CanvasState.Completed)
            .Permit(CanvasTrigger.Fail, CanvasState.Failed)
            .Permit(CanvasTrigger.Cancel, CanvasState.Cancelled)
            .Permit(CanvasTrigger.Abandon, CanvasState.Abandoned)
            .Permit(CanvasTrigger.Expire, CanvasState.Expired);

        foreach (var closed in (CanvasState[])[CanvasState.Completed, CanvasState.Failed, CanvasState.Cancelled, CanvasState.Abandoned, CanvasState.Expired])
        {
            machine.Configure(closed).SubstateOf(CanvasState.Closed);
        }

        return machine;
    }
}
