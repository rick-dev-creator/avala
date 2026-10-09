using Avala.Canvas.Canvases;
using Avala.Canvas.Contracts;

namespace Avala.Canvas.Gallery;

internal static class Snapshots
{
    extension(CanvasDocument document)
    {
        public CanvasSnapshot Snapshot =>
            new(document.Id, document.Session, document.Title, document.MediaType, document.Content, document.State.Status, document.IsOffered);
    }

    extension(CanvasState state)
    {
        public CanvasStatus Status => state switch
        {
            CanvasState.Completed => CanvasStatus.Completed,
            CanvasState.Failed => CanvasStatus.Failed,
            CanvasState.Cancelled => CanvasStatus.Cancelled,
            CanvasState.Abandoned => CanvasStatus.Abandoned,
            CanvasState.Expired => CanvasStatus.Expired,
            _ => CanvasStatus.Streaming,
        };
    }
}
