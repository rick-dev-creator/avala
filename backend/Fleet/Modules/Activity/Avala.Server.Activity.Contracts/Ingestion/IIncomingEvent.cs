namespace Avala.Server.Activity.Contracts.Ingestion;

internal interface IIncomingEvent
{
    Guid Id { get; }

    DateTimeOffset OccurredAt { get; }
}
