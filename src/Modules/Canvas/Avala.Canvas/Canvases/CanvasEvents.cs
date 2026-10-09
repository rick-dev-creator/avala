using Avala.Canvas.Contracts;
using Avala.Sdk;
using Avala.Sdk.Domain;

namespace Avala.Canvas.Canvases;

internal sealed record CanvasOpened(CanvasId Canvas, Option<CanvasError> Rejection) : IDomainEvent;

internal sealed record ContentAppended(CanvasId Canvas, int Length) : IDomainEvent;

internal sealed record CanvasClosed(CanvasId Canvas, CanvasState State) : IDomainEvent;
