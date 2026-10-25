using Avala.Server.Violating.Contracts;
using FastEndpoints;
using Mediator;

namespace Avala.Server.Violating.EndPoints.Placing;

internal sealed record PlaceOrderResponse(ArchivedOrder Order);

internal sealed class PlaceOrderEndpoint(ISender sender) : Endpoint<PlaceOrder, PlaceOrderResponse>
{
    public override void Configure()
    {
        Post("/orders");
        AllowAnonymous();
    }

    public override async Task HandleAsync(PlaceOrder req, CancellationToken ct)
    {
        await sender.Send(req, ct);
        var archived = await sender.Send(new ArchiveOrder(Guid.Empty), ct);
        await Send.OkAsync(new PlaceOrderResponse(archived.Value), ct);
    }
}
