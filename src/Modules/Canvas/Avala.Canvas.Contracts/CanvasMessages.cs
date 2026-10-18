using Avala.Agents.Contracts.Sessions;
using Avala.Sdk.Events;

namespace Avala.Canvas.Contracts;

public enum CanvasStatus
{
    Streaming,
    Completed,
    Failed,
    Cancelled,
    Abandoned,
    Expired,
}

public sealed record CanvasSnapshot(
    CanvasId Canvas,
    SessionId Session,
    string Title,
    string MediaType,
    string Content,
    CanvasStatus Status,
    bool IsOffered);

public sealed record CanvasUpdated(CanvasSnapshot Snapshot) : IIntegrationEvent;

public sealed record MediaShown(SessionId Session, TurnId Turn, ItemId Item, Attachment Media, string Caption) : IIntegrationEvent;
