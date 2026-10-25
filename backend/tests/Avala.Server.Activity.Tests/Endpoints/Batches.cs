using System.Security.Claims;
using System.Text.Json;
using Avala.Server.Activity.EndPoints.Ingestion;

namespace Avala.Server.Activity.Tests.Endpoints;

internal static class Batches
{
    public static readonly Guid Instance = Guid.Parse("0199f6a0-0000-7000-8000-000000000003");

    public static readonly Guid Job = Guid.Parse("0199f6a0-0000-7000-8000-000000000010");

    public const string Time = "2026-10-25T12:00:00Z";

    public static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public static ClaimsPrincipal Caller { get; } = new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, Instance.ToString()), new Claim(ClaimTypes.Name, "desktop")],
        "ApiKey"));

    public static Guid Id(int number) => Guid.Parse($"0199f6a0-0000-7000-8000-{number:D12}");

    public static string Progressed(int id, string status = "\"running\"") =>
        $$"""{ "kind": "jobProgressed", "id": "{{Id(id)}}", "occurredAt": "{{Time}}", "job": "{{Job}}", "status": {{status}} }""";

    public static IngestEventsRequest Request(params string[] events) =>
        new([.. events.Select(text => JsonDocument.Parse(text).RootElement.Clone())]);

    public static MappedBatch Map(params string[] events) => IngestEventsMapper.ToCommand(Caller, Request(events));
}
