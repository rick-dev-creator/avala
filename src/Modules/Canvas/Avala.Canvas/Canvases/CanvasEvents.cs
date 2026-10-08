using Avala.Canvas.Contracts;
using Avala.Sdk.Domain;

namespace Avala.Canvas.Canvases;

internal sealed record ContentAppended(CanvasId Canvas, int Length) : IDomainEvent;

internal sealed record CanvasClosed(CanvasId Canvas, CanvasState State) : IDomainEvent;
