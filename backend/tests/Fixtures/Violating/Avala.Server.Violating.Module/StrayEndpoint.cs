using FastEndpoints;

namespace Avala.Server.Violating.Module;

internal sealed record StrayResponse(string Status);

internal sealed class StrayEndpoint : EndpointWithoutRequest<StrayResponse>
{
    public override void Configure() => Get("/stray");

    public override Task HandleAsync(CancellationToken ct) => Send.OkAsync(new StrayResponse("lost"), ct);
}
