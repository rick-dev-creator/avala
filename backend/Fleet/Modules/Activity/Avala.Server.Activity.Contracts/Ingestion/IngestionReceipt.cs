namespace Avala.Server.Activity.Contracts.Ingestion;

internal sealed record IngestionReceipt(IReadOnlyList<Guid> Accepted, IReadOnlyList<Guid> Duplicates, IReadOnlyList<RejectedEvent> Rejected);

internal sealed record RejectedEvent(Guid Id, string Reason);
