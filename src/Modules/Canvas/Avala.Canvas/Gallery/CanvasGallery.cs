using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Canvases;
using Avala.Canvas.Contracts;
using Avala.Sdk;

namespace Avala.Canvas.Gallery;

internal sealed class CanvasGallery : ICanvases
{
    private readonly Lock gate = new();
    private readonly OrderedDictionary<CanvasId, CanvasDocument> documents = [];

    public Result<CanvasId, CanvasError> Open(CanvasStarted started)
    {
        lock (gate)
        {
            return documents.ContainsKey(new CanvasId(started.Turn, started.Item))
                ? CanvasError.AlreadyOpen
                : CanvasDocument.Open(started).Map(document =>
                {
                    documents.Add(document.Id, document);

                    return document.Id;
                });
        }
    }

    public Result<CanvasId, CanvasError> Append(ItemProgressed progressed)
    {
        lock (gate)
        {
            return Find(new CanvasId(progressed.Turn, progressed.Item))
                .Bind(document => document.Append(progressed))
                .Map(appended => appended.Canvas);
        }
    }

    public Result<CanvasId, CanvasError> Close(ItemCompleted completed)
    {
        lock (gate)
        {
            return Find(new CanvasId(completed.Turn, completed.Item))
                .Bind(document => document.Close(completed))
                .Map(closed => closed.Canvas);
        }
    }

    public Option<CanvasSnapshot> Snapshot(CanvasId canvas)
    {
        lock (gate)
        {
            return documents.TryGetValue(canvas, out var document) ? document.Snapshot : Option<CanvasSnapshot>.None;
        }
    }

    public IReadOnlyList<CanvasSnapshot> InSession(SessionId session)
    {
        lock (gate)
        {
            return [.. documents.Values.Where(document => document.Session == session).Select(document => document.Snapshot)];
        }
    }

    private Result<CanvasDocument, CanvasError> Find(CanvasId canvas) =>
        documents.TryGetValue(canvas, out var document) ? document : CanvasError.UnknownCanvas;
}
