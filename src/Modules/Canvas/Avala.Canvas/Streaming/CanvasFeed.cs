using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Canvas.Canvases;
using Avala.Canvas.Contracts;
using Avala.Canvas.Gallery;
using Avala.Canvas.Throttling;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.Logging;

namespace Avala.Canvas.Streaming;

internal sealed partial class CanvasFeed(CanvasGallery gallery, SnapshotThrottle throttle, ILogger<CanvasFeed> logger)
    : IHandle<AgentActivity>
{
    public ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken) =>
        integrationEvent.Event switch
        {
            CanvasStarted started => ChangedAsync(gallery.Open(started), cancellationToken),
            ItemProgressed progressed => ChangedAsync(gallery.Append(progressed), cancellationToken),
            ItemCompleted completed => FinishedAsync(gallery.Close(completed), cancellationToken),
            _ => ValueTask.CompletedTask,
        };

    private ValueTask ChangedAsync(Result<CanvasId, CanvasError> applied, CancellationToken cancellationToken) =>
        Accepted(applied, out var canvas) ? throttle.ChangedAsync(canvas, cancellationToken) : ValueTask.CompletedTask;

    private ValueTask FinishedAsync(Result<CanvasId, CanvasError> applied, CancellationToken cancellationToken) =>
        Accepted(applied, out var canvas) ? throttle.FinishedAsync(canvas, cancellationToken) : ValueTask.CompletedTask;

    private bool Accepted(Result<CanvasId, CanvasError> applied, out CanvasId canvas)
    {
        if (applied.TryGetValue(out canvas, out var error))
        {
            return true;
        }

        if (error != CanvasError.UnknownCanvas)
        {
            LogRejected(error);
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected canvas content: {Rejection}")]
    private partial void LogRejected(CanvasError rejection);
}
