namespace Avala.Server.Activity.Domain;

internal sealed record ReceivedEvent(Guid InstanceId, Guid EventId, DateTimeOffset ReceivedAt);
