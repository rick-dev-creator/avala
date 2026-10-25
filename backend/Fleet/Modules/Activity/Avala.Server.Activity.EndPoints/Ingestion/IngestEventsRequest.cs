using System.Text.Json;

namespace Avala.Server.Activity.EndPoints.Ingestion;

internal sealed record IngestEventsRequest(IReadOnlyList<JsonElement>? Events);
