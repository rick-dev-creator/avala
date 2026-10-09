using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Canvases;
using Avala.Canvas.Contracts;
using Avala.Sdk;

namespace Avala.Canvas.Gallery;

internal sealed class CanvasGallery : ICanvases
{
    private readonly OrderedDictionary<CanvasId, CanvasDocument> documents = [];
    private ImmutableList<CanvasSnapshot> snapshots = [];

    public Result<CanvasId, CanvasError> Open(CanvasStarted started) =>
        documents.ContainsKey(new CanvasId(started.Turn, started.Item))
            ? CanvasError.AlreadyOpen
            : CanvasDocument.Open(started).Map(document =>
            {
                documents.Add(document.Id, document);
                Volatile.Write(ref snapshots, snapshots.Add(document.Snapshot));

                return document.Id;
            });

    public Result<CanvasId, CanvasError> Append(ItemProgressed progressed) =>
        Find(new CanvasId(progressed.Turn, progressed.Item))
            .Bind(document => document.Append(progressed).Map(appended => Published(document, appended.Canvas)));

    public Result<CanvasId, CanvasError> Close(ItemCompleted completed) =>
        Find(new CanvasId(completed.Turn, completed.Item))
            .Bind(document => document.Close(completed).Map(closed => Published(document, closed.Canvas)));

    public Option<CanvasSnapshot> Snapshot(CanvasId canvas) =>
        Volatile.Read(ref snapshots).Find(snapshot => snapshot.Canvas == canvas).ToOption();

    public IReadOnlyList<CanvasSnapshot> InSession(SessionId session) =>
        [.. Volatile.Read(ref snapshots).Where(snapshot => snapshot.Session == session)];

    private CanvasId Published(CanvasDocument document, CanvasId canvas)
    {
        Volatile.Write(ref snapshots, snapshots.SetItem(documents.IndexOf(canvas), document.Snapshot));

        return canvas;
    }

    private Result<CanvasDocument, CanvasError> Find(CanvasId canvas) =>
        documents.TryGetValue(canvas, out var document) ? document : CanvasError.UnknownCanvas;
}
