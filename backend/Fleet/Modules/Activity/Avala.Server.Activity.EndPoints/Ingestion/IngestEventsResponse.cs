namespace Avala.Server.Activity.EndPoints.Ingestion;

internal sealed record IngestEventsResponse(
    IReadOnlyList<Guid> Accepted,
    IReadOnlyList<Guid> Duplicates,
    IReadOnlyList<IngestEventsEntry> Deferred,
    IReadOnlyList<IngestEventsEntry> Rejected);

internal sealed record IngestEventsEntry(string Id, string Reason);
