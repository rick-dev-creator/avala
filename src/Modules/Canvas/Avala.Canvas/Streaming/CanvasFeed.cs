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
    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        switch (integrationEvent.Event)
        {
            case CanvasStarted started when Accepted(gallery.Open(started), out var canvas):
                await throttle.ChangedAsync(canvas, cancellationToken);
                break;
            case ItemProgressed progressed when Accepted(gallery.Append(progressed), out var canvas):
                await throttle.ChangedAsync(canvas, cancellationToken);
                break;
            case ItemCompleted completed when Accepted(gallery.Close(completed), out var canvas):
                await throttle.FinishedAsync(canvas, cancellationToken);
                break;
        }
    }

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
