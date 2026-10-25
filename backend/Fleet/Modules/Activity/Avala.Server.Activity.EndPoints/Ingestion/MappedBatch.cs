using Avala.Server.Activity.Contracts.Ingestion;

namespace Avala.Server.Activity.EndPoints.Ingestion;

internal sealed record MappedBatch(IngestEvents Command, IReadOnlyList<IngestEventsEntry> Rejected, IReadOnlyList<IngestEventsEntry> Deferred);
